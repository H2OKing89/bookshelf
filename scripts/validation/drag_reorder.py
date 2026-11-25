"""Verify desktop and touch profile reordering in an owned application home."""

from copy import deepcopy
import json
from pathlib import Path

from . import app, common, identity, upgrade


BROWSER = Path(__file__).with_name("drag-reorder-browser.cjs")


def item_id(item):
    return item["quality"]["id"] if item.get("quality") else item["id"]


def item_name(item):
    return item["quality"]["name"] if item.get("quality") else item["name"]


def run(runtime, output, playwright_module):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    completed, failure, stage = False, None, "configure"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        fixture.check("fixture_has_no_download_clients_or_indexers", not fixture.api("downloadclient") and not fixture.api("indexer"))

        stage = "seed_profiles"
        quality = deepcopy(fixture.api("qualityprofile")[0])
        quality.update(id=0, name="Owned reorder profile")
        quality = fixture.api("qualityprofile", "POST", quality)
        quality = fixture.api(f"qualityprofile/{quality['id']}")
        items = quality["items"]
        fixture.check("quality_profile_has_adjacent_items", len(items) >= 2 and item_id(items[-1]) != item_id(items[-2]))

        base = fixture.api("delayprofile/1")
        delay_profiles = []
        for label, delay in (("owned-drag-first", 17), ("owned-drag-second", 29)):
            tag = fixture.api("tag", "POST", {"label": label})
            profile = fixture.api("delayprofile", "POST", {
                **base, "id": 0, "tags": [tag["id"]], "usenetDelay": delay, "torrentDelay": delay
            })
            delay_profiles.append({"id": profile["id"], "label": label})
        delay_initial = fixture.api("delayprofile")
        fixture.check("delay_profiles_have_stable_distinct_orders",
                      [p["id"] for p in sorted(delay_initial, key=lambda p: p["order"]) if p["id"] != 1] ==
                      [p["id"] for p in delay_profiles])

        stage = "browser"
        config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                  "username": fixture.current_username, "password": fixture.current_password,
                  "apiKey": fixture.api_key, "output": str(common.private_directory(output / "screenshots")),
                  "playwrightModule": str(Path(playwright_module).resolve(strict=True)),
                  "qualityProfile": {"id": quality["id"], "name": quality["name"]},
                  "qualityItems": [{"id": item_id(item), "name": item_name(item)} for item in items],
                  "delayProfiles": delay_profiles}
        execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
            raise common.ValidationFailure("drag_reorder_browser_checks_invalid")
        fixture.checks.update(checks)
        common.write_json(output / "browser-result.json", report)
        fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        stage = "verify_saved_state"
        fixture.check("opposite_profile_moves_restore_original_quality_and_fields", fixture.api(f"qualityprofile/{quality['id']}") == quality)
        fixture.check("opposite_delay_moves_restore_original_order_and_fields",
                      sorted(fixture.api("delayprofile"), key=lambda p: p["id"]) == sorted(delay_initial, key=lambda p: p["id"]))
        fixture.check("default_delay_profile_unchanged", fixture.api("delayprofile/1") == base)
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
    finally:
        fixture.cleanup()
        fixture.checks["owned_process_stopped"] = fixture.process is None and fixture.forced_stops == 0
    result = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", result)
    return result
