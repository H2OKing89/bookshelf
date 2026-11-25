"""Verify list tag ownership and explicit non-destructive cleanup in disposable homes."""

from datetime import datetime, timezone
import json
from pathlib import Path
import sqlite3

from . import app, common, identity, library_review, restore, upgrade
from .collection_diagnostics import (FIRST_LIST, FIRST_NAME, SECOND_LIST, SECOND_NAME,
                                     ListEndpoint, seed_list, sync_list)

BROWSER = Path(__file__).with_name("list-lifecycle-browser.cjs")
AUTHOR = 11
MANUAL_TAG = 401
SYNC_TAG = 402
CLEANUP_TAG = 403
MANAGED_BOOK = 101
MANUALLY_REASSERTED_BOOK = 102
LOCAL_BOOK = 104
MANUAL_BOOK = 105


def seed(fixture, first, second):
    fixture.stop()
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        seed_list(fixture, database, FIRST_LIST, FIRST_NAME, first)
        seed_list(fixture, database, SECOND_LIST, SECOND_NAME, second)
        for tag_id, label in [(MANUAL_TAG, "owned-manual"), (SYNC_TAG, "owned-list"), (CLEANUP_TAG, "owned-former-list")]:
            restore.insert(database, "Tags", {"Id": tag_id, "Label": label})
        database.execute('UPDATE "Authors" SET "Tags"=? WHERE "Id"=?', (json.dumps([MANUAL_TAG]), AUTHOR))
        database.execute('UPDATE "Books" SET "Monitored"=1')
    fixture.start()


def set_policy(fixture):
    # Old homes have no provenance. Model two previously confirmed list-created
    # recordings in this *owned* test home after the real additive migration.
    # Core tests exercise origin capture at insertion, including invalidation
    # by subsequent user choices; migration must never claim baseline records.
    fixture.stop()
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        if database.execute('SELECT COUNT(*) FROM "ImportListManagedRecordings"').fetchone()[0]:
            raise common.ValidationFailure("unexpected_legacy_recording_origin")
        if database.execute('SELECT COUNT(*) FROM "ImportListTagOwners"').fetchone()[0]:
            raise common.ValidationFailure("unexpected_legacy_tag_ownership")
        database.execute('UPDATE "ImportLists" SET "TagExisting"=2,"Tags"=? WHERE "Id" IN (?,?)',
                         (json.dumps([SYNC_TAG]), FIRST_LIST, SECOND_LIST))
        for book_id in (MANAGED_BOOK, MANUALLY_REASSERTED_BOOK):
            database.execute('UPDATE "Books" SET "ImportListOriginId"=? WHERE "Id"=?', (FIRST_LIST, book_id))
            database.execute('''INSERT INTO "ImportListManagedRecordings" ("BookId","ForeignBookId","AuthorMetadataId","ImportListId")
                                SELECT "Id","ForeignBookId","AuthorMetadataId",? FROM "Books" WHERE "Id"=?''', (FIRST_LIST, book_id))
    fixture.start()


def tags(fixture):
    return set(fixture.api(f"author/{AUTHOR}")["tags"])


def claims(fixture):
    with sqlite3.connect(fixture.home / "readarr.db") as database:
        return database.execute('SELECT "AuthorId","TagId","ImportListId" FROM "ImportListTagOwners" ORDER BY "AuthorId","TagId","ImportListId"').fetchall()


def book_states(fixture):
    return {book["id"]: book["monitored"] for book in fixture.api("book")}


def apply_cleanup(fixture, preview):
    return fixture.api("importlistcleanup", "POST", {key: preview.get(key) for key in ("mode", "tagId", "confirmationToken")})


def inspect_policies(fixture, first, second):
    fixture.check("cleanup_requires_authentication", library_review.request(fixture, "importlistcleanup", key=False)[0] == 401)
    initial = fixture.api("importlistcleanup?mode=unmonitor")
    fixture.check("unfetched_lists_block_cleanup", bool(initial["blockers"]) and not initial.get("confirmationToken") and not initial["candidates"])
    start_states = book_states(fixture)
    fixture.check("initial_book_states_are_monitored", all(start_states[book] for book in (MANAGED_BOOK, MANUALLY_REASSERTED_BOOK, LOCAL_BOOK, MANUAL_BOOK)))
    sync_list(fixture, FIRST_LIST)
    sync_list(fixture, SECOND_LIST)
    fixture.check("both_lists_apply_one_sync_tag_to_existing_author", tags(fixture) == {MANUAL_TAG, SYNC_TAG} and
                  claims(fixture) == [(AUTHOR, SYNC_TAG, FIRST_LIST), (AUTHOR, SYNC_TAG, SECOND_LIST)])
    first.book_ids = []
    sync_list(fixture, FIRST_LIST)
    fixture.check("another_list_retains_shared_tag", tags(fixture) == {MANUAL_TAG, SYNC_TAG})
    second.mode = "partial"
    sync_list(fixture, SECOND_LIST)
    fixture.check("partial_refresh_does_not_strip_list_tags_or_authorize_cleanup", tags(fixture) == {MANUAL_TAG, SYNC_TAG} and
                  bool(fixture.api("importlistcleanup?mode=unmonitor")["blockers"]))
    second.mode = "failed"
    sync_list(fixture, SECOND_LIST)
    fixture.check("failed_refresh_does_not_strip_list_tags_or_authorize_cleanup", tags(fixture) == {MANUAL_TAG, SYNC_TAG} and
                  bool(fixture.api("importlistcleanup?mode=unmonitor")["blockers"]))
    second.mode = "success"
    second.book_ids = []
    sync_list(fixture, SECOND_LIST)
    fixture.check("complete_empty_snapshots_remove_only_sync_owned_tag", tags(fixture) == {MANUAL_TAG} and not claims(fixture))

    second.book_ids = [MANUAL_BOOK]
    sync_list(fixture, SECOND_LIST)
    fixture.check("returning_member_can_supply_sync_tag_again", tags(fixture) == {MANUAL_TAG, SYNC_TAG} and
                  claims(fixture) == [(AUTHOR, SYNC_TAG, SECOND_LIST)])
    # A deliberate Add of an already-applied tag establishes manual ownership;
    # the same-value monitoring edit also relinquishes cleanup control.
    status, _ = library_review.request(fixture, "author/editor", "PUT", {"authorIds": [AUTHOR], "tags": [SYNC_TAG], "applyTags": "add"})
    fixture.check("manual_same_value_tag_edit_releases_claim", status in (200, 202) and not claims(fixture))
    status, _ = library_review.request(fixture, "book/editor", "PUT", {"bookIds": [MANUALLY_REASSERTED_BOOK], "monitored": True})
    fixture.check("manual_same_value_book_edit_accepted", status in (200, 202))
    second.book_ids = []
    sync_list(fixture, SECOND_LIST)
    fixture.check("manually_adopted_tag_survives_successful_empty_list", tags(fixture) == {MANUAL_TAG, SYNC_TAG})
    fixture.check("sync_does_not_modify_monitoring_when_disabled", book_states(fixture) == start_states)

    preview = fixture.api("importlistcleanup?mode=unmonitor")
    fixture.check("only_exact_previously_list_managed_recording_is_eligible", not preview["blockers"] and
                  [candidate["bookId"] for candidate in preview["candidates"]] == [MANAGED_BOOK] and bool(preview["confirmationToken"]))
    no_action = fixture.api("importlistcleanup")
    fixture.check("preview_only_cannot_be_applied_and_does_not_modify_monitoring", [candidate["bookId"] for candidate in no_action["candidates"]] == [MANAGED_BOOK] and
                  not no_action.get("confirmationToken") and book_states(fixture) == start_states)
    return start_states, preview


def browser(fixture, output, playwright_module):
    config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
              "username": fixture.current_username, "password": fixture.current_password,
              "output": str(common.private_directory(output / "screenshots")), "playwrightModule": str(playwright_module),
              "firstList": {"id": FIRST_LIST, "name": FIRST_NAME}}
    execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
    report = json.loads(execution.stdout)
    checks = report.get("checks")
    if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
        raise common.ValidationFailure("list_lifecycle_browser_report_invalid")
    fixture.checks.update(checks)
    common.write_json(output / "browser-result.json", report)
    fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))


def inspect_apply(fixture, first, second, start_states, preview):
    tag_preview = fixture.api(f"importlistcleanup?mode=tag&tagId={CLEANUP_TAG}")
    fixture.check("tag_preview_explicitly_names_affected_author", len(tag_preview["candidates"]) == 1 and
                  tag_preview["candidates"][0]["authorId"] == AUTHOR)
    tag_receipt = apply_cleanup(fixture, tag_preview)
    fixture.check("confirmed_cleanup_tag_is_permanent_and_leaves_all_books_monitored", not tag_receipt.get("confirmationToken") and
                  tags(fixture) == {MANUAL_TAG, SYNC_TAG, CLEANUP_TAG} and not claims(fixture) and book_states(fixture) == start_states)
    status, _ = library_review.request(fixture, "importlistcleanup", "POST", {key: preview.get(key) for key in ("mode", "tagId", "confirmationToken")})
    fixture.check("changed_tag_state_invalidates_older_unmonitor_preview", status == 409 and book_states(fixture) == start_states)
    preview = fixture.api("importlistcleanup?mode=unmonitor")
    second.mode = "failed"
    sync_list(fixture, SECOND_LIST)
    status, _ = library_review.request(fixture, "importlistcleanup", "POST", {key: preview.get(key) for key in ("mode", "tagId", "confirmationToken")})
    fixture.check("failed_provider_invalidates_old_confirmation_even_when_membership_retained", status == 409 and book_states(fixture) == start_states)
    second.mode = "success"
    sync_list(fixture, SECOND_LIST)
    latest = fixture.api("importlistcleanup?mode=unmonitor")
    receipt = apply_cleanup(fixture, latest)
    expected = dict(start_states)
    expected[MANAGED_BOOK] = False
    fixture.check("confirmed_unmonitor_only_changes_one_list_managed_recording", [candidate["bookId"] for candidate in receipt["candidates"]] == [MANAGED_BOOK] and
                  not receipt.get("confirmationToken") and book_states(fixture) == expected and restore.media_unchanged(fixture))
    fixture.check("applied_cleanup_no_longer_offer_same_recording", not fixture.api("importlistcleanup?mode=unmonitor")["candidates"])
    fixture.stop()
    fixture.start()
    fixture.check("restart_preserves_tags_monitoring_and_released_cleanup_claim", tags(fixture) == {MANUAL_TAG, SYNC_TAG, CLEANUP_TAG} and
                  book_states(fixture) == expected and not fixture.api("importlistcleanup?mode=unmonitor")["candidates"] and restore.media_unchanged(fixture))


def run(runtime, output, baseline_runtime, playwright_module):
    runtime, output = common.validate_output(runtime, output)
    baseline_runtime, _ = common.validate_output(baseline_runtime, output)
    if baseline_runtime == runtime:
        raise common.ValidationFailure("list_lifecycle_requires_older_package")
    common.private_directory(output)
    playwright_module = Path(playwright_module).resolve(strict=True)
    contract = identity.recipe()
    first, second = ListEndpoint(contract, [MANAGED_BOOK, MANUALLY_REASSERTED_BOOK]), ListEndpoint(contract, [MANAGED_BOOK, MANUAL_BOOK])
    fixture = app.Fixture(baseline_runtime, output / "application")
    completed, failure, stage = False, None, "baseline"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        identity.seed(fixture, contract)
        seed(fixture, first, second)
        baseline = identity.snapshot(fixture)
        fixture.stop()
        stage = "upgrade"
        fixture.runtime = runtime
        fixture.start()
        fixture.check("upgrade_preserves_legacy_catalog_and_media", identity.snapshot(fixture, baseline) == baseline and restore.media_unchanged(fixture))
        fixture.check("new_tag_policy_defaults_none_for_existing_lists", all(fixture.api(f"importlist/{list_id}")["tagExisting"] == "none" for list_id in (FIRST_LIST, SECOND_LIST)))
        fixture.check("no_real_clients_or_indexers_configured", not fixture.api("downloadclient") and not fixture.api("indexer"))
        set_policy(fixture)
        stage = "policies"
        states, preview = inspect_policies(fixture, first, second)
        stage = "browser"
        browser(fixture, output, playwright_module)
        stage = "apply"
        inspect_apply(fixture, first, second, states, preview)
        # Except for the explicitly confirmed monitor flag and author tags,
        # compare pre-upgrade catalog/edition/file values and identities exactly.
        final = identity.snapshot(fixture, baseline)
        # The existing bulk author editor writes DateTime values using the
        # provider's SQLite encoding (space/Z rather than ISO T/+00:00).
        # Preserve their precise instant, including fractional seconds, instead
        # of mistaking that lexical round trip for an identity or data change.
        date_columns = [baseline["Authors"]["columns"].index(column) for column in ("Added", "LastInfoSync")]
        for snapshot in (baseline, final):
            rows = []
            for row in snapshot["Authors"]["rows"]:
                row = list(row)
                for column in date_columns:
                    if row[column] is not None:
                        row[column] = datetime.fromisoformat(row[column]).astimezone(timezone.utc).isoformat()
                rows.append(tuple(row))
            snapshot["Authors"]["rows"] = rows
        fixture.check("manual_author_tag_edit_retains_exact_timestamp_instants",
                      [[row[column] for column in date_columns] for row in baseline["Authors"]["rows"]] ==
                      [[row[column] for column in date_columns] for row in final["Authors"]["rows"]])
        for table, fields in {"Books": ["Monitored"], "Authors": ["Tags"]}.items():
            projection = baseline[table]
            columns = [i for i, column in enumerate(projection["columns"]) if column not in fields]
            projection["columns"] = [projection["columns"][i] for i in columns]
            projection["rows"] = [tuple(row[i] for i in columns) for row in projection["rows"]]
            final[table]["columns"] = projection["columns"]
            final[table]["rows"] = [tuple(row[i] for i in columns) for row in final[table]["rows"]]
        if final != baseline:
            differences = {}
            for table in baseline:
                if final[table] == baseline[table]:
                    continue
                left, right = baseline[table], final[table]
                differences[table] = {
                    "rowsBefore": len(left["rows"]), "rowsAfter": len(right["rows"]),
                    "changedColumns": sorted({column for a, b in zip(left["rows"], right["rows"])
                                              for column, x, y in zip(left["columns"], a, b) if x != y})
                }
            common.write_json(output / "projection-differences.json", differences)
        fixture.check("cleanup_preserves_catalog_recording_and_file_identity", final == baseline and restore.media_unchanged(fixture))
        fixture.check("owned_provider_routes_only", all(set(item.requests) <= {
            "/api/v1/author", "/api/v1/book", "/api/v1/qualityprofile", "/api/v1/tag", "/api/v1/rootfolder"} for item in (first, second)))
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
        fixture.checks[stage + "_completed"] = False
    finally:
        fixture.cleanup()
        first.close()
        second.close()
        fixture.checks["owned_processes_stopped"] = fixture.process is None and fixture.forced_stops == 0 and not first.thread.is_alive() and not second.thread.is_alive()
    report = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", report)
    return report
