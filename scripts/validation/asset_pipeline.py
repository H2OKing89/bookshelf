"""Verify package-local frontend assets, including worker loading under UrlBase."""

import json
from pathlib import Path

from . import app, common, identity, restore, upgrade


BROWSER = Path(__file__).with_name("asset-pipeline-browser.cjs")


def verify_mode(runtime, output, playwright_module, url_base):
    common.private_directory(output)
    fixture = app.Fixture(runtime, output / "application")
    fixture.URL_BASE = url_base
    completed, failure, stage = False, None, "configure"
    try:
        fixture.configure_home()
        fixture.start()
        upgrade.seed_current_account(fixture)
        contract = identity.recipe()
        identity.seed(fixture, contract)
        recording = next(case for case in contract["cases"] if case["region"] is None)
        author = fixture.api("author/11")
        config = {"url": "http://" + fixture.backend_host + fixture.URL_BASE,
                  "username": fixture.current_username, "password": fixture.current_password,
                  "output": str(common.private_directory(output / "screenshots")),
                  "playwrightModule": str(playwright_module),
                  "recording": {"title": recording["title"], "slug": "synthetic-identity-" + recording["key"]},
                  "author": {"name": author["authorName"]}}
        stage = "browser"
        execution = identity._browser_process(config, common.private_directory(output / "browser-process"), BROWSER)
        report = json.loads(execution.stdout)
        checks = report.get("checks")
        if not isinstance(checks, dict) or not checks or not all(type(value) is bool for value in checks.values()):
            raise common.ValidationFailure("asset_browser_checks_invalid")
        fixture.checks.update(checks)
        common.write_json(output / "browser-result.json", report)
        fixture.check("browser_completed", execution.returncode == 0 and report.get("completed") is True and all(checks.values()))
        fixture.check("owned_media_unchanged", restore.media_unchanged(fixture))
        completed = True
    except Exception as error:
        failure = app.safe_failure(stage, error)
    finally:
        fixture.cleanup()
        fixture.checks["owned_process_stopped"] = fixture.process is None and fixture.forced_stops == 0
    value = app.result(fixture, completed, failure=failure)
    common.write_json(output / "result.json", value)
    return value


def run(runtime, output, playwright_module):
    runtime, output = common.validate_output(runtime, output)
    common.private_directory(output)
    playwright_module = Path(playwright_module).resolve(strict=True)
    results = {name: verify_mode(runtime, output / name, playwright_module, base)
               for name, base in (("root", ""), ("subpath", "/bookshelf-validation"))}
    checks = {f"{name}_{key}": value for name, entry in results.items() for key, value in entry["checks"].items()}
    passed = sum(checks.values())
    return {"checks": checks, "passed": passed, "failed": len(checks) - passed,
            "completed": all(entry["completed"] for entry in results.values()), "syntheticHomesOnly": True}
