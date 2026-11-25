"""Owned upgrade and settings fixture for health-restored and media-server connections.

Delivery transitions and committed multipart scan counts are exercised in the
source fixtures. Here, migrate real connection rows from an older package,
check opt-in/default/legacy API semantics, and use a loopback Subsonic server
to verify that saved settings and the explicit Test operation still work.
No real providers, metadata endpoints or user homes are used.
"""

from pathlib import Path
import json

from . import app, common, identity, library_review, provider_policies, upgrade


BROWSER = Path(__file__).with_name("notification-lifecycle-browser.cjs")
RECOVERY = "Owned recovery webhook"
PLEX = "Owned Plex connection"
SUBSONIC = "Owned Subsonic connection"


class NotificationEndpoint(provider_policies.OwnedEndpoint):
    def response(self, method, route, headers, stream):
        if method == "GET" and route.path == "/rest/ping":
            return 200, "application/xml", '<subsonic-response status="ok" version="1.15.0" />', {}
        return self.reject()


def create_connection(fixture, implementation, name, values):
    schema = next(row for row in fixture.api("notification/schema") if row["implementation"] == implementation)
    for key in schema:
        if key.startswith("on") and key[2:3].isupper():
            schema[key] = False
    for field in schema["fields"]:
        if field["name"] in values:
            field["value"] = values[field["name"]]
    schema.update(name=name, tags=[])
    created = fixture.api("notification?forceSave=true", "POST", schema)
    return fixture.api(f"notification/{created['id']}")


def fields(resource):
    # Never print endpoint settings or credential values to the report.
    return {field["name"]: field.get("value") for field in resource["fields"]}


def legacy_projection(resource):
    return {key: value for key, value in resource.items() if key not in ("onHealthRestored", "supportsOnHealthRestored")}


def inspect_connections(fixture, old):
    current = {name: fixture.api(f"notification/{item['id']}") for name, item in old.items()}
    fixture.check("migration_preserves_connection_rows_settings_and_existing_triggers",
                  all(legacy_projection(row) == old[name] for name, row in current.items()))
    fixture.check("migration_defaults_all_restored_triggers_to_false",
                  all(row["onHealthRestored"] is False for row in current.values()))
    schema = fixture.api("notification/schema")
    fixture.check("schema_declares_capability_and_off_default",
                  all(row.get("onHealthRestored") is False and type(row.get("supportsOnHealthRestored")) is bool for row in schema) and
                  current[RECOVERY]["supportsOnHealthRestored"] is True and current[SUBSONIC]["supportsOnHealthRestored"] is True and
                  current[PLEX]["supportsOnHealthRestored"] is False)
    hook = current[RECOVERY]
    route = f"notification/{hook['id']}"
    fixture.check("connection_policy_requires_authentication", library_review.request(fixture, route, key=False)[0] == 401)
    fixture.api(route + "?forceSave=true", "PUT", dict(hook, onHealthRestored=True, includeHealthWarnings=True))
    saved = fixture.api(route)
    fixture.check("recovery_only_opt_in_is_saved_independently", saved["onHealthRestored"] is True and
                  saved["onHealthIssue"] is False and saved["includeHealthWarnings"] is True)
    omitted = {key: value for key, value in saved.items() if key != "onHealthRestored"}
    fixture.api(route + "?forceSave=true", "PUT", omitted)
    fixture.check("legacy_client_omission_preserves_enabled_restored_trigger", fixture.api(route) == saved)
    fixture.api(route + "?forceSave=true", "PUT", dict(saved, onHealthRestored=False))
    fixture.check("explicit_false_disables_restored_trigger", fixture.api(route)["onHealthRestored"] is False)
    fixture.api(route + "?forceSave=true", "PUT", saved)
    fixture.check("unrelated_connections_remain_unchanged",
                  all(fixture.api(f"notification/{current[name]['id']}") == current[name] for name in (PLEX, SUBSONIC)))
    return {**current, RECOVERY: fixture.api(route)}


def run(runtime, output, baseline_runtime, playwright_module=None):
    runtime, output = common.validate_output(runtime, output)
    baseline_runtime, _ = common.validate_output(baseline_runtime, output)
    if baseline_runtime == runtime:
        raise common.ValidationFailure("notification_lifecycle_requires_older_package")
    common.private_directory(output)
    fixture = app.Fixture(baseline_runtime, output / "application")
    endpoint = NotificationEndpoint()
    completed, failure, stage = False, None, "baseline"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        port = endpoint.server.server_port
        old = {
            RECOVERY: create_connection(fixture, "Webhook", RECOVERY, {"url": endpoint.url + "/owned-health"}),
            PLEX: create_connection(fixture, "PlexServer", PLEX, {"host": "127.0.0.1", "port": port,
                                                                      "mapFrom": "/owned", "mapTo": "/mapped", "updateLibrary": False}),
            SUBSONIC: create_connection(fixture, "Subsonic", SUBSONIC, {"host": "127.0.0.1", "port": port,
                                                                      "notify": True, "updateLibrary": True})
        }
        fixture.check("older_package_has_no_restored_trigger", all("onHealthRestored" not in row for row in old.values()))
        fixture.stop()
        stage = "upgrade"
        fixture.runtime = runtime
        fixture.start()
        saved = inspect_connections(fixture, old)
        fixture.check("media_server_settings_preserved", fields(saved[PLEX])["updateLibrary"] is False and
                      fields(saved[SUBSONIC])["updateLibrary"] is True and fields(saved[SUBSONIC])["notify"] is True)
        fixture.check("disabled_media_servers_are_not_contacted", not endpoint.requests)
        stage = "subsonic_test"
        # Explicit connection testing performs only a loopback version ping.
        code, _ = library_review.request(fixture, "notification/test", "POST", saved[SUBSONIC])
        fixture.check("subsonic_manual_test_still_works", code == 200 and
                      endpoint.requests[("GET", "/rest/ping")] == 1 and endpoint.unexpected == 0)
        if playwright_module:
            stage = "browser"
            config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                      "username": fixture.current_username, "password": fixture.current_password,
                      "output": str(common.private_directory(output / "screenshots")),
                      "playwrightModule": str(Path(playwright_module).resolve(strict=True)),
                      "recoveryName": RECOVERY, "plexName": PLEX, "subsonicName": SUBSONIC}
            execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
            report = json.loads(execution.stdout)
            checks = report.get("checks")
            if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
                raise common.ValidationFailure("notification_lifecycle_browser_report_invalid")
            fixture.checks.update(checks)
            common.write_json(output / "browser-result.json", report)
            fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        stage = "restart"
        fixture.stop()
        fixture.start()
        fixture.check("restored_opt_in_and_media_settings_survive_restart",
                      all(fixture.api(f"notification/{row['id']}") == row for row in saved.values()))
        fixture.check("no_unsolicited_network_deliveries", sum(endpoint.requests.values()) == 1 and endpoint.unexpected == 0)
        fixture.check("no_real_download_clients_or_active_indexers", not fixture.api("downloadclient") and
                      all(not any(row[key] for key in ("enableRss", "enableAutomaticSearch", "enableInteractiveSearch")) for row in fixture.api("indexer")))
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
