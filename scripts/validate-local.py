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


CHECKS = ("package", "app", "restore", "upgrade", "media", "cancellation", "datastore", "sqlite", "logstorage", "postgresconnections", "authpolicy", "docker")


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--runtime", required=True, type=Path, help="Complete native Linux package; never an application data home")
    result.add_argument("--baseline-runtime", type=Path, help="Explicit older package for a genuine same-home upgrade check")
    result.add_argument("--legacy-runtime", type=Path, help="Optional older package which generates a genuine legacy backup")
    result.add_argument("--docker-runtime", type=Path, help="Optional native-architecture musl package for the actual Dockerfile checks")
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
        if any(name in chosen for name in ("app", "restore", "upgrade", "media", "cancellation", "datastore", "sqlite", "logstorage", "postgresconnections", "authpolicy")):
            common.ensure_host_compatible(inputs["candidate"])
        for label in ("baseline", "legacy"):
            if label in inputs:
                common.ensure_host_compatible(inputs[label])
        # Imports are deliberately after validation; modules never launch on import.
        from validation import app, authpolicy, cancellation, datastore, docker, logstorage, media, package, postgresconnections, restore, sqlite, upgrade
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
            "authpolicy": lambda: authpolicy.run(inputs["candidate"], output / "authpolicy", baseline_runtime=inputs["baseline"]),
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
