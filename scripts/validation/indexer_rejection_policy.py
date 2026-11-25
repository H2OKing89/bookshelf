"""Verify D08 policy migration, API preservation and advanced settings in an owned home.

The candidate never contacts a real indexer or download client. A disabled
synthetic RSS definition points at a counting loopback endpoint, and the
source regression fixtures independently exercise rejection transactions.
"""

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
from threading import Thread

from . import app, common, identity, library_review, upgrade


BROWSER = Path(__file__).with_name("indexer-rejection-policy-browser.cjs")
POLICY_FIELDS = ("dangerousFileAction", "executableFileAction", "userRejectedExtensionAction")
DEFAULT = dict.fromkeys(POLICY_FIELDS, "review")
CHOSEN = dict(zip(POLICY_FIELDS, ("review", "blocklistAndRetry", "blocklist")))
FIRST_NAME = "Owned rejection policy indexer"
SECOND_NAME = "Owned default policy indexer"


class DisabledEndpoint:
    def __init__(self):
        self.requests = 0
        endpoint = self

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, format, *args):
                pass

            def reject(self):
                endpoint.requests += 1
                self.send_response(503)
                self.send_header("Content-Length", "0")
                self.end_headers()

            do_GET = reject
            do_POST = reject
            do_HEAD = reject

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.thread = Thread(target=self.server.serve_forever, kwargs={"poll_interval": 0.05}, daemon=True)
        self.thread.start()

    @property
    def url(self):
        return f"http://127.0.0.1:{self.server.server_port}/owned/feed"

    def close(self):
        self.server.shutdown()
        self.server.server_close()
        self.thread.join(timeout=5)


def create_disabled(fixture, endpoint, name):
    schema = next(row for row in fixture.api("indexer/schema") if row["implementation"] == "TorrentRssIndexer")
    for field in schema["fields"]:
        if field["name"] == "baseUrl":
            field["value"] = endpoint.url
    schema.update(name=name, enableRss=False, enableAutomaticSearch=False, enableInteractiveSearch=False, priority=25, tags=[])
    created = fixture.api("indexer?forceSave=true", "POST", schema)
    return fixture.api(f"indexer/{created['id']}")


def policy(resource):
    return {key: resource.get(key) for key in POLICY_FIELDS}


def existing_projection(resource):
    keys = ("id", "name", "implementation", "configContract", "enableRss", "enableAutomaticSearch", "enableInteractiveSearch", "priority", "tags", "downloadClientId", "protocol")
    return {**{key: resource.get(key) for key in keys},
            "fields": {field["name"]: field.get("value") for field in resource["fields"]}}


def inspect_policies(fixture, first, second):
    one, two = f"indexer/{first['id']}", f"indexer/{second['id']}"
    current = fixture.api(one)
    fixture.check("upgrade_keeps_indexer_identity_and_settings", existing_projection(current) == existing_projection(first) and
                  existing_projection(fixture.api(two)) == existing_projection(second))
    fixture.check("old_indexers_default_to_review", policy(current) == DEFAULT and policy(fixture.api(two)) == DEFAULT)
    fixture.check("indexer_schema_defaults_to_review", all(policy(schema) == DEFAULT for schema in fixture.api("indexer/schema")))
    fixture.check("indexer_policy_requires_authentication", library_review.request(fixture, one, key=False)[0] == 401)

    fixture.api(one + "?forceSave=true", "PUT", dict(current, **CHOSEN))
    saved = fixture.api(one)
    fixture.check("exact_per_indexer_choices_round_trip", policy(saved) == CHOSEN and policy(fixture.api(two)) == DEFAULT)
    fixture.check("policy_only_edit_preserves_disabled_state_and_configuration", existing_projection(saved) == existing_projection(first))

    # An older v1 client updates a saved definition without knowing any of the
    # new fields. Omission retains them; an explicit zero/review resets one.
    old_client = {key: value for key, value in saved.items() if key not in POLICY_FIELDS}
    old_client["priority"] = 24
    fixture.api(one + "?forceSave=true", "PUT", old_client)
    saved = fixture.api(one)
    fixture.check("legacy_update_without_policy_fields_preserves_choices", saved["priority"] == 24 and policy(saved) == CHOSEN)
    fixture.api(one + "?forceSave=true", "PUT", dict(saved, executableFileAction="review"))
    reset = fixture.api(one)
    fixture.check("explicit_review_choice_is_not_mistaken_for_omission", policy(reset) == dict(CHOSEN, executableFileAction="review"))

    invalid_before = fixture.api(one)
    fixture.check("unsupported_numeric_policy_is_rejected", library_review.request(fixture, one + "?forceSave=true", "PUT", dict(invalid_before, executableFileAction=999))[0] == 400)
    fixture.check("unsupported_string_policy_is_rejected", library_review.request(fixture, one + "?forceSave=true", "PUT", dict(invalid_before, executableFileAction="notAnAction"))[0] == 400)
    fixture.check("invalid_policy_does_not_modify_saved_settings", fixture.api(one) == invalid_before)
    fixture.api(one + "?forceSave=true", "PUT", dict(reset, **CHOSEN))
    fixture.api("indexer/bulk", "PUT", {"ids": [first["id"], second["id"]], "priority": 26})
    fixture.check("unrelated_bulk_edit_preserves_each_indexers_choices", fixture.api(one)["priority"] == 26 and fixture.api(two)["priority"] == 26 and
                  policy(fixture.api(one)) == CHOSEN and policy(fixture.api(two)) == DEFAULT)
    return fixture.api(one), fixture.api(two)


def run(runtime, output, baseline_runtime, playwright_module=None):
    runtime, output = common.validate_output(runtime, output)
    baseline_runtime, _ = common.validate_output(baseline_runtime, output)
    if baseline_runtime == runtime:
        raise common.ValidationFailure("indexer_rejection_policy_requires_older_package")
    common.private_directory(output)
    fixture = app.Fixture(baseline_runtime, output / "application")
    endpoint = DisabledEndpoint()
    completed, failure, stage = False, None, "baseline"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        first = create_disabled(fixture, endpoint, FIRST_NAME)
        second = create_disabled(fixture, endpoint, SECOND_NAME)
        fixture.check("older_package_has_no_rejection_policy", all(field not in first and field not in second for field in POLICY_FIELDS))
        fixture.stop()
        stage = "upgrade"
        fixture.runtime = runtime
        fixture.start()
        saved_one, saved_two = inspect_policies(fixture, first, second)
        if playwright_module:
            stage = "browser"
            config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                      "username": fixture.current_username, "password": fixture.current_password,
                      "output": str(common.private_directory(output / "screenshots")),
                      "playwrightModule": str(Path(playwright_module).resolve(strict=True)), "indexerName": FIRST_NAME,
                      "choices": CHOSEN}
            execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
            report = json.loads(execution.stdout)
            checks = report.get("checks")
            if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
                raise common.ValidationFailure("indexer_rejection_browser_report_invalid")
            fixture.checks.update(checks)
            common.write_json(output / "browser-result.json", report)
            fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        stage = "restart"
        fixture.stop()
        fixture.start()
        fixture.check("restart_preserves_disabled_indexers_and_different_policies", fixture.api(f"indexer/{first['id']}") == saved_one and fixture.api(f"indexer/{second['id']}") == saved_two)
        fixture.check("no_real_download_clients_or_active_indexers", not fixture.api("downloadclient") and
                      all(not any(row[key] for key in ("enableRss", "enableAutomaticSearch", "enableInteractiveSearch")) for row in fixture.api("indexer")))
        fixture.check("disabled_owned_endpoint_was_not_contacted", endpoint.requests == 0)
        completed = True
    except Exception as error:
        fixture.checks[stage + "_completed"] = False
        failure = app.safe_failure(stage, error)
    finally:
        fixture.cleanup()
        endpoint.close()
        fixture.checks["owned_processes_stopped"] = fixture.process is None and fixture.forced_stops == 0 and not endpoint.thread.is_alive()
    report = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", report)
    return report
