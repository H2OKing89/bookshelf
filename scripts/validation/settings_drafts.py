"""Verify settings saves and unsaved-change protection in an owned browser."""

import json
from pathlib import Path

from . import app, common, identity, upgrade


BROWSER = Path(__file__).with_name("settings-drafts-browser.cjs")
CLIENT_NAME = "Synthetic Disabled Client"
CONFIG_RESOURCE = "config/downloadclient"


def run(runtime, output, playwright_module):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    playwright_module = Path(playwright_module).resolve(strict=True)
    completed, failure, stage = False, None, "configure"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        fixture.check("fixture_has_no_download_clients_or_indexers", not fixture.api("downloadclient")
                      and not fixture.api("indexer"))
        original = fixture.api(CONFIG_RESOURCE)
        fixture.check("fixture_uses_default_download_handling", original["enableCompletedDownloadHandling"] is True)
        profiles = fixture.api("qualityprofile")
        fixture.check("fixture_has_a_saved_zero_quality_threshold", any(item["minUpgradeFormatScore"] == 0 for item in profiles))
        client = next(item for item in fixture.api("downloadclient/schema") if item["implementation"] == "QBittorrent")
        client.update(name=CLIENT_NAME, enable=False)
        next(field for field in client["fields"] if field["name"] == "username")["value"] = "synthetic-disabled-username"
        next(field for field in client["fields"] if field["name"] == "password")["value"] = "synthetic-disabled-password"
        client = fixture.api("downloadclient?forceSave=true", "POST", client)

        stage = "browser"
        config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                  "username": fixture.current_username, "password": fixture.current_password,
                  "output": str(common.private_directory(output / "screenshots")),
                  "playwrightModule": str(playwright_module), "downloadClientName": CLIENT_NAME}
        execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
            raise common.ValidationFailure("settings_browser_checks_invalid")
        fixture.checks.update(checks)
        common.write_json(output / "browser-result.json", report)
        fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))

        stage = "verify_database"
        expected = {**original, "enableCompletedDownloadHandling": False}
        fixture.check("only_the_reviewed_global_option_changed", fixture.api(CONFIG_RESOURCE) == expected)
        fixture.check("disabled_client_and_its_credentials_are_unchanged", fixture.api(f"downloadclient/{client['id']}") == client)
        fixture.check("unrelated_profiles_preserve_the_saved_zero_threshold", fixture.api("qualityprofile") == profiles)
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
    finally:
        fixture.cleanup()
        fixture.checks["owned_process_stopped"] = fixture.process is None and fixture.forced_stops == 0
    result = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", result)
    return result
