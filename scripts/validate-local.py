#!/usr/bin/env python3
"""Validate delivery packages locally using disposable synthetic data."""

import argparse
from datetime import datetime, timezone
import os
from pathlib import Path
import signal
import sys
import tempfile

from validation import common


CHECKS = ("package", "app", "restore", "upgrade", "media", "cancellation", "datastore", "sqlite", "logstorage", "postgresconnections", "authpolicy", "accesspolicy", "releaseidentity", "acquisition", "identity", "authoridentitypromotion", "metadataaccessibility", "libraryreview", "historypaging", "historysync", "wantedpaging", "queuepaging", "settingsdrafts", "routernavigation", "interactivesearch", "libraryvirtualization", "assetpipeline", "recordinglifecycle", "collectiondiagnostics", "listlifecycle", "providerpolicies", "indexerrejectionpolicy", "qualityranking", "hosttlslifecycle", "downloadqueue", "apidocuments", "notificationlifecycle", "performanceprofiling", "dragreorder", "docker")


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--runtime", required=True, type=Path, help="Complete native Linux package; never an application data home")
    result.add_argument("--baseline-runtime", type=Path, help="Explicit older package for a genuine same-home upgrade check")
    result.add_argument("--legacy-runtime", type=Path, help="Optional older package which generates a genuine legacy backup")
    result.add_argument("--docker-runtime", type=Path, help="Optional native-architecture musl package for the actual Dockerfile checks")
    result.add_argument("--playwright-module", type=Path, help="Installed Playwright module for owned browser checks")
    result.add_argument("--checks", choices=CHECKS, nargs="+", help="Default: package app restore media; upgrade/Docker added when their package arguments are supplied")
    result.add_argument("--output", type=Path, help="A NEW evidence directory; default: _temp/validation/run-<random>")
    return result


def main(argv=None):
    arguments = parser().parse_args(argv)
    repo = Path(__file__).resolve().parents[1]
    chosen = list(dict.fromkeys(arguments.checks or ["package", "app", "restore", "media"]))
    if arguments.checks is None:
        if arguments.baseline_runtime:
            chosen.append("upgrade")
        if arguments.docker_runtime:
            chosen.append("docker")
    if "upgrade" in chosen and not arguments.baseline_runtime:
        parser().error("--checks upgrade requires --baseline-runtime")
    if "datastore" in chosen and not arguments.baseline_runtime:
        parser().error("--checks datastore requires --baseline-runtime (a pre-P12 package)")
    if "postgresconnections" in chosen and not arguments.baseline_runtime:
        parser().error("--checks postgresconnections requires --baseline-runtime (a pre-P14 package)")
    if "authpolicy" in chosen and not arguments.baseline_runtime:
        parser().error("--checks authpolicy requires --baseline-runtime (a pre-P16 package)")
    for check in ("identity", "recordinglifecycle", "collectiondiagnostics", "listlifecycle", "qualityranking", "notificationlifecycle"):
        if check in chosen and (not arguments.baseline_runtime or not arguments.playwright_module):
            parser().error(f"--checks {check} requires --baseline-runtime and --playwright-module")
    for check in ("providerpolicies", "indexerrejectionpolicy"):
        if check in chosen and not arguments.baseline_runtime:
            parser().error(f"--checks {check} requires --baseline-runtime")
    if "metadataaccessibility" in chosen and not arguments.playwright_module:
        parser().error("--checks metadataaccessibility requires --playwright-module")
    if "libraryreview" in chosen and not arguments.playwright_module:
        parser().error("--checks libraryreview requires --playwright-module")
    if "historypaging" in chosen and not arguments.playwright_module:
        parser().error("--checks historypaging requires --playwright-module")
    if "historysync" in chosen and not arguments.playwright_module:
        parser().error("--checks historysync requires --playwright-module")
    for check in ("wantedpaging", "queuepaging", "settingsdrafts", "routernavigation", "interactivesearch", "libraryvirtualization", "assetpipeline", "performanceprofiling", "dragreorder"):
        if check in chosen and not arguments.playwright_module:
            parser().error(f"--checks {check} requires --playwright-module")
    if "docker" in chosen and not arguments.docker_runtime:
        parser().error("--checks docker requires --docker-runtime (a musl package)")
    previous_umask = os.umask(0o077)
    previous_term_handler = signal.getsignal(signal.SIGTERM)
    def interrupted(signum, frame):
        raise KeyboardInterrupt()
    signal.signal(signal.SIGTERM, interrupted)
    interrupted_run = False
    output = None
    results = {}
    inputs = {}
    snapshots = {}
    try:
        for label, value in (("candidate", arguments.runtime), ("baseline", arguments.baseline_runtime),
                             ("legacy", arguments.legacy_runtime), ("docker", arguments.docker_runtime)):
            if value is not None:
                inputs[label] = common.resolve_runtime(value)
        if "baseline" in inputs and inputs["baseline"] == inputs["candidate"]:
            raise common.ValidationFailure("upgrade_requires_distinct_baseline_package")
        if arguments.output is None:
            parent = repo / "_temp" / "validation"
            for runtime in inputs.values():
                common.validate_output(runtime, parent / "new-run")
            parent.mkdir(mode=0o700, parents=True, exist_ok=True)
            if parent.is_symlink() or parent.stat().st_mode & 0o077:
                raise common.ValidationFailure("private_output_parent_required")
            output = Path(tempfile.mkdtemp(prefix="run-", dir=parent))
        else:
            for runtime in inputs.values():
                common.validate_output(runtime, arguments.output)
            output = common.private_directory(arguments.output.expanduser().resolve())
        for label, runtime in inputs.items():
            snapshots[label] = common.snapshot_runtime(runtime)
        if any(name in chosen for name in ("app", "restore", "upgrade", "media", "cancellation", "datastore", "sqlite", "logstorage", "postgresconnections", "authpolicy", "accesspolicy", "releaseidentity", "acquisition", "identity", "authoridentitypromotion", "metadataaccessibility", "libraryreview", "historypaging", "historysync", "wantedpaging", "queuepaging", "settingsdrafts", "routernavigation", "interactivesearch", "libraryvirtualization", "assetpipeline", "recordinglifecycle", "collectiondiagnostics", "listlifecycle", "providerpolicies", "indexerrejectionpolicy", "qualityranking", "hosttlslifecycle", "downloadqueue", "apidocuments", "notificationlifecycle", "performanceprofiling", "dragreorder")):
            common.ensure_host_compatible(inputs["candidate"])
        for label in ("baseline", "legacy"):
            if label in inputs:
                common.ensure_host_compatible(inputs[label])
        # Imports are deliberately after validation; modules never launch on import.
        from validation import accesspolicy, acquisition, app, asset_pipeline, authpolicy, cancellation, collection_diagnostics, datastore, docker, history_paging, identity, indexer_rejection_policy, interactive_search, library_review, list_lifecycle, library_virtualization, logstorage, media, metadata_accessibility, operational_paging, package, postgresconnections, provider_policies, quality_ranking, releaseidentity, recording_lifecycle, restore, router_navigation, settings_drafts, sqlite, upgrade
        from validation import api_documents, author_identity_promotion, drag_reorder, download_queue, host_tls_lifecycle, notification_lifecycle, performance_profiling
        callbacks = {
            "package": lambda: package.run(inputs["candidate"], output / "package", repo),
            "app": lambda: app.run(inputs["candidate"], output / "app"),
            "restore": lambda: restore.run(inputs["candidate"], output / "restore", legacy_runtime=inputs.get("legacy")),
            "upgrade": lambda: upgrade.run(inputs["candidate"], output / "upgrade", baseline_runtime=inputs["baseline"]),
            "media": lambda: media.run(inputs["candidate"], output / "media"),
            "cancellation": lambda: cancellation.run(inputs["candidate"], output / "cancellation"),
            "datastore": lambda: datastore.run(inputs["candidate"], output / "datastore", baseline_runtime=inputs["baseline"]),
            "sqlite": lambda: sqlite.run(inputs["candidate"], output / "sqlite", baseline_runtime=inputs.get("baseline")),
            "logstorage": lambda: logstorage.run(inputs["candidate"], output / "logstorage"),
            "postgresconnections": lambda: postgresconnections.run(inputs["candidate"], output / "postgresconnections", baseline_runtime=inputs["baseline"]),
            "accesspolicy": lambda: accesspolicy.run(inputs["candidate"], output / "accesspolicy"),
            "releaseidentity": lambda: releaseidentity.run(inputs["candidate"], output / "releaseidentity"),
            "acquisition": lambda: acquisition.run(inputs["candidate"], output / "acquisition"),
            "identity": lambda: identity.run(inputs["candidate"], output / "identity", baseline_runtime=inputs["baseline"], playwright_module=arguments.playwright_module),
            "authoridentitypromotion": lambda: author_identity_promotion.run(inputs["candidate"], output / "authoridentitypromotion"),
            "metadataaccessibility": lambda: metadata_accessibility.run(inputs["candidate"], output / "metadataaccessibility", arguments.playwright_module),
            "libraryreview": lambda: library_review.run(inputs["candidate"], output / "libraryreview", arguments.playwright_module),
            "historypaging": lambda: history_paging.run(inputs["candidate"], output / "historypaging", arguments.playwright_module),
            "historysync": lambda: history_paging.run_sync(inputs["candidate"], output / "historysync", arguments.playwright_module),
            "wantedpaging": lambda: operational_paging.run_wanted(inputs["candidate"], output / "wantedpaging", arguments.playwright_module),
            "queuepaging": lambda: operational_paging.run_queue(inputs["candidate"], output / "queuepaging", arguments.playwright_module),
            "settingsdrafts": lambda: settings_drafts.run(inputs["candidate"], output / "settingsdrafts", arguments.playwright_module),
            "routernavigation": lambda: router_navigation.run(inputs["candidate"], output / "routernavigation", arguments.playwright_module),
            "interactivesearch": lambda: interactive_search.run(inputs["candidate"], output / "interactivesearch", arguments.playwright_module),
            "libraryvirtualization": lambda: library_virtualization.run(inputs["candidate"], output / "libraryvirtualization", arguments.playwright_module),
            "assetpipeline": lambda: asset_pipeline.run(inputs["candidate"], output / "assetpipeline", arguments.playwright_module),
            "recordinglifecycle": lambda: recording_lifecycle.run(inputs["candidate"], output / "recordinglifecycle", inputs["baseline"], arguments.playwright_module),
            "collectiondiagnostics": lambda: collection_diagnostics.run(inputs["candidate"], output / "collectiondiagnostics", inputs["baseline"], arguments.playwright_module),
            "listlifecycle": lambda: list_lifecycle.run(inputs["candidate"], output / "listlifecycle", inputs["baseline"], arguments.playwright_module),
            "providerpolicies": lambda: provider_policies.run(inputs["candidate"], output / "providerpolicies", inputs["baseline"], arguments.playwright_module),
            "indexerrejectionpolicy": lambda: indexer_rejection_policy.run(inputs["candidate"], output / "indexerrejectionpolicy", inputs["baseline"], arguments.playwright_module),
            "qualityranking": lambda: quality_ranking.run(inputs["candidate"], output / "qualityranking", inputs["baseline"], arguments.playwright_module),
            "authpolicy": lambda: authpolicy.run(inputs["candidate"], output / "authpolicy", baseline_runtime=inputs["baseline"]),
            "hosttlslifecycle": lambda: host_tls_lifecycle.run(inputs["candidate"], output / "hosttlslifecycle", inputs.get("baseline")),
            "downloadqueue": lambda: download_queue.run(inputs["candidate"], output / "downloadqueue"),
            "apidocuments": lambda: api_documents.run(inputs["candidate"], output / "apidocuments"),
            "notificationlifecycle": lambda: notification_lifecycle.run(inputs["candidate"], output / "notificationlifecycle", inputs["baseline"], arguments.playwright_module),
            "dragreorder": lambda: drag_reorder.run(inputs["candidate"], output / "dragreorder", arguments.playwright_module),
            "performanceprofiling": lambda: performance_profiling.run(inputs["candidate"], output / "performanceprofiling", arguments.playwright_module),
            "docker": lambda: docker.run(inputs["docker"], output / "docker", repo),
        }
        for name in chosen:
            try:
                results[name] = callbacks[name]()
                value = results[name]
                if (not isinstance(value, dict) or type(value.get("completed")) is not bool or
                    type(value.get("failed")) is not int or value["failed"] < 0 or
                    type(value.get("passed")) is not int or value["passed"] < 0 or
                    not isinstance(value.get("checks"), dict) or
                    not all(type(item) is bool for item in value["checks"].values()) or
                    (value["completed"] and not value["checks"]) or
                    value["failed"] != sum(not item for item in value["checks"].values()) or
                    value["passed"] != sum(value["checks"].values())):
                    raise common.ValidationFailure("invalid_check_report")
            except Exception as error:
                code = error.code if isinstance(error, common.ValidationFailure) else "check_execution_failed"
                results[name] = common.failure_result(code, error)
            value = results[name]
            print(f"{name}: {value.get('passed', 0)} passed, {value.get('failed', 0)} failed" +
                  (", skipped" if value.get("skipped") is True else ""), flush=True)
    except (Exception, KeyboardInterrupt) as error:
        interrupted_run = isinstance(error, KeyboardInterrupt)
        code = error.code if isinstance(error, common.ValidationFailure) else "validation_interrupted" if interrupted_run else "validation_execution_failed"
        results["runner"] = common.failure_result(code, error)
        print(f"validation: {code}", file=sys.stderr)
    finally:
        for label, before in snapshots.items():
            try:
                unchanged = common.snapshot_runtime(inputs[label]) == before
            except Exception:
                unchanged = False
            results["unchanged_" + label] = {"checks": {"input_package_unchanged": unchanged},
                                           "passed": int(unchanged), "failed": int(not unchanged), "completed": True}
        failed = sum(value.get("failed", 1) for value in results.values())
        incomplete = any(not value.get("completed") or value.get("skipped") is True for value in results.values())
        try:
            if output is not None:
                report = {"timestampUtc": datetime.now(timezone.utc).isoformat(), "checksRequested": chosen,
                          "results": results, "passed": sum(value.get("passed", 0) for value in results.values()),
                          "failed": failed, "completed": not failed and not incomplete,
                          "inputFingerprints": {label: common.runtime_fingerprint(value) for label, value in snapshots.items()},
                          "notRequested": ([name for name in CHECKS if name not in chosen] +
                                           (["genuine_legacy_restore"] if "restore" in chosen and "legacy" not in inputs else [])),
                          "syntheticDataOnly": True, "externalPortsPublished": False, "githubWorkflowsUsed": False,
                          "interrupted": interrupted_run}
                common.write_json(output / "summary.json", report)
                print(f"Evidence: {output}")
        except (Exception, KeyboardInterrupt) as error:
            interrupted_run = interrupted_run or isinstance(error, KeyboardInterrupt)
            failed += 1
            print("validation: private_report_write_failed", file=sys.stderr)
        finally:
            os.umask(previous_umask)
            signal.signal(signal.SIGTERM, previous_term_handler)
    return 130 if interrupted_run else 1 if failed or incomplete else 0


if __name__ == "__main__":
    raise SystemExit(main())
