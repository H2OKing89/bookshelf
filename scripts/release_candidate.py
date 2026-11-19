"""Local release-candidate staging. This never enables updates or publishes.

Receipts bind tested bytes to compiled source metadata and the corresponding
Git source archive. They are local attestations, not signed update manifests.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile

from validation.common import (ValidationFailure, file_sha256, private_directory,
                               runtime_fingerprint, snapshot_runtime, validate_output, write_json)
from validation.package import runtime_details
from validation.releaseidentity import PRODUCT_ID, SOURCE_FIELDS, valid_identity


REQUIRED_CHECKS = {"package", "app", "restore", "upgrade", "media", "cancellation", "authpolicy", "accesspolicy", "releaseidentity"}
ALLOWED_RIDS = {"linux-x64"}
CHANNEL = "develop"


def require(condition, code):
    if not condition:
        raise ValidationFailure(code)


def git(repo, *args):
    result = subprocess.run(["git", *args], cwd=repo, capture_output=True, timeout=60)
    require(result.returncode == 0, "source_git_operation_failed")
    return result.stdout.decode().strip()


def read_json(path):
    require(path.is_file() and not path.is_symlink() and path.stat().st_size <= 8 * 1024 * 1024,
            "bounded_regular_json_required")
    return json.loads(path.read_text())


def archive_sha256(repo, commit):
    digest = hashlib.sha256()
    with subprocess.Popen(["git", "archive", "--format=tar.gz", commit], cwd=repo,
                          stdout=subprocess.PIPE, stderr=subprocess.DEVNULL) as process:
        for block in iter(lambda: process.stdout.read(1024 * 1024), b""):
            digest.update(block)
        require(process.wait(timeout=60) == 0, "source_archive_failed")
    return digest.hexdigest()


def schema_identity(repo):
    migrations = {}
    for path in sorted((repo / "src/NzbDrone.Core/Datastore/Migration").glob("[0-9]*.cs")):
        migrations[path.name] = file_sha256(path)
    require(bool(migrations), "source_migrations_required")
    return {"migrationFiles": migrations, "fingerprint": hashlib.sha256(
        json.dumps(migrations, sort_keys=True, separators=(",", ":")).encode()).hexdigest()}


def verified_reports(paths, fingerprint):
    require(0 < len(paths) <= 16, "bounded_evidence_list_required")
    reports, covered, source, version = [], set(), None, None
    for path in paths:
        report = read_json(path)
        require(report.get("completed") is True and type(report.get("failed")) is int and report["failed"] == 0
                and report.get("syntheticDataOnly") is True, "successful_synthetic_evidence_required")
        require(report.get("inputFingerprints", {}).get("candidate") == fingerprint,
                "evidence_does_not_match_package")
        results = report.get("results")
        require(isinstance(results, dict), "validation_results_required")
        for name, entry in results.items():
            require(isinstance(entry, dict) and entry.get("completed") is True and entry.get("skipped") is not True
                    and type(entry.get("failed")) is int and entry["failed"] == 0
                    and isinstance(entry.get("checks"), dict) and bool(entry["checks"])
                    and all(value is True for value in entry["checks"].values())
                    and type(entry.get("passed")) is int and entry["passed"] == len(entry["checks"]), "complete_passing_checks_required")
            if name in REQUIRED_CHECKS:
                covered.add(name)
            if name == "releaseidentity":
                current = entry.get("metadata", {})
                require(source in (None, current.get("source")) and version in (None, current.get("version")),
                        "conflicting_identity_evidence")
                source, version = current.get("source"), current.get("version")
        require(type(report.get("passed")) is int and report["passed"] == sum(entry["passed"] for entry in results.values()),
                "consistent_evidence_counts_required")
        reports.append(report)
    require(REQUIRED_CHECKS <= covered, "required_release_checks_missing")
    require(isinstance(source, dict) and valid_identity(source) and source["buildDirty"] is False,
            "clean_compiled_source_identity_required")
    require(isinstance(version, str) and re.fullmatch(r"\d+\.\d+\.\d+\.\d+", version), "compiled_version_required")
    return reports, source, version


def prepare(runtime, evidence, output, repo, channel=CHANNEL):
    require(channel == CHANNEL, "unsupported_release_channel")
    runtime, output = validate_output(runtime, output)
    require(not output.exists(), "new_release_output_required")
    require(not git(repo, "status", "--porcelain", "--untracked-files=normal"), "clean_source_checkout_required")
    before = snapshot_runtime(runtime)
    fingerprint = runtime_fingerprint(before)
    reports, source, version = verified_reports(evidence, fingerprint)
    require(source["sourceCommit"] == git(repo, "rev-parse", "HEAD")
            and source["sourceTree"] == git(repo, "rev-parse", "HEAD^{tree}")
            and source["sourceBranch"] == git(repo, "branch", "--show-current"), "compiled_source_does_not_match_checkout")
    require(read_json(runtime / "UI/source-provenance.json") == source, "frontend_source_mismatch")
    details = runtime_details(runtime)
    require(details["rid"] in ALLOWED_RIDS and details["tfm"] == "net10.0", "unverified_release_runtime")
    require(set(details["frameworks"]) == {"Microsoft.NETCore.App", "Microsoft.AspNetCore.App"}
            and len(set(details["frameworks"].values())) == 1
            and all(value.startswith("10.0.") for value in details["frameworks"].values()), "unexpected_release_framework")
    schema = schema_identity(repo)
    private_directory(output)
    try:
        shutil.copytree(runtime, output / "package")
        require(snapshot_runtime(output / "package") == before, "staged_package_mismatch")
        archive = output / "source.tar.gz"
        result = subprocess.run(["git", "archive", "--format=tar.gz", "--output=" + str(archive), source["sourceCommit"]],
                                cwd=repo, capture_output=True, timeout=120)
        require(result.returncode == 0, "source_archive_failed")
        with tarfile.open(archive, "r:gz") as tar:
            require("LICENSE.md" in tar.getnames() and tar.pax_headers.get("comment") == source["sourceCommit"],
                    "matching_gpl_source_archive_required")
        receipts = {}
        for index, report in enumerate(reports):
            name = "evidence-" + str(index + 1) + ".json"
            write_json(output / name, report)
            receipts[name] = file_sha256(output / name)
        manifest = {"schemaVersion": 1, "purpose": "local-release-candidate", "productId": PRODUCT_ID,
                    "channel": channel, "publishable": False, "builtInUpdatesEnabled": False,
                    "version": version, "runtime": details, "database": schema,
                    "source": dict(source, archive="source.tar.gz", archiveSha256=file_sha256(archive), license="GPL-3.0"),
                    "package": {"directory": "package", "fingerprint": fingerprint, "files": before}, "evidence": receipts}
        write_json(output / "release-manifest.json", manifest)
        require(snapshot_runtime(runtime) == before, "input_package_changed")
        require(not git(repo, "status", "--porcelain", "--untracked-files=normal")
                and source["sourceCommit"] == git(repo, "rev-parse", "HEAD"), "source_changed_during_staging")
        verify(output, repo)
        return manifest
    except BaseException:
        shutil.rmtree(output)
        raise


def verify(directory, repo, *, expected_product=PRODUCT_ID, expected_channel=CHANNEL,
           expected_commit=None, expected_rid=None, expected_version=None):
    require(not git(repo, "status", "--porcelain", "--untracked-files=normal"), "clean_source_checkout_required")
    directory = Path(directory).resolve()
    manifest = read_json(directory / "release-manifest.json")
    require(type(manifest.get("schemaVersion")) is int and manifest["schemaVersion"] == 1
            and manifest.get("purpose") == "local-release-candidate"
            and manifest.get("publishable") is False and manifest.get("builtInUpdatesEnabled") is False,
            "unsupported_release_manifest")
    require(manifest.get("productId") == expected_product == PRODUCT_ID, "release_product_mismatch")
    require(manifest.get("channel") == expected_channel == CHANNEL, "release_channel_mismatch")
    package = manifest.get("package", {})
    require(package.get("directory") == "package", "invalid_package_path")
    require(not (directory / "package").is_symlink(), "package_directory_must_not_be_symlink")
    snapshot = snapshot_runtime(directory / "package")
    fingerprint = runtime_fingerprint(snapshot)
    require(snapshot == package.get("files") and fingerprint == package.get("fingerprint"), "release_payload_mismatch")
    details = runtime_details(directory / "package")
    require(details == manifest.get("runtime") and details["rid"] in ALLOWED_RIDS
            and (expected_rid is None or details["rid"] == expected_rid), "release_runtime_mismatch")
    source = manifest.get("source", {})
    identity = {name: source.get(name) for name in SOURCE_FIELDS}
    require(valid_identity(identity) and identity["buildDirty"] is False
            and identity["productId"] == PRODUCT_ID, "invalid_release_source_identity")
    require(source["sourceCommit"] == (expected_commit or git(repo, "rev-parse", "HEAD"))
            and source["sourceCommit"] == git(repo, "rev-parse", "HEAD")
            and source["sourceTree"] == git(repo, "rev-parse", source["sourceCommit"] + "^{tree}")
            and source["sourceBranch"] == git(repo, "branch", "--show-current"), "release_source_mismatch")
    require(source.get("archive") == "source.tar.gz" and source.get("license") == "GPL-3.0"
            and not (directory / "source.tar.gz").is_symlink()
            and file_sha256(directory / "source.tar.gz") == source.get("archiveSha256")
            and source["archiveSha256"] == archive_sha256(repo, source["sourceCommit"]), "release_source_archive_mismatch")
    with tarfile.open(directory / "source.tar.gz", "r:gz") as tar:
        require(tar.pax_headers.get("comment") == source["sourceCommit"] and "LICENSE.md" in tar.getnames(),
                "matching_gpl_source_archive_required")
    require(read_json(directory / "package/UI/source-provenance.json") == identity, "release_frontend_mismatch")
    require(manifest.get("database") == schema_identity(repo), "release_database_schema_mismatch")
    entries = manifest.get("evidence", {})
    require(isinstance(entries, dict) and bool(entries), "release_evidence_required")
    for name, digest in entries.items():
        require(re.fullmatch(r"evidence-[1-9][0-9]*\.json", name) and file_sha256(directory / name) == digest,
                "release_evidence_mismatch")
    require({entry.name for entry in directory.iterdir()} ==
            {"package", "source.tar.gz", "release-manifest.json", *entries}, "unexpected_release_bundle_files")
    _, attested, version = verified_reports([directory / name for name in entries], fingerprint)
    require(attested == identity and version == manifest.get("version")
            and (expected_version is None or version == expected_version), "release_version_or_identity_mismatch")
    return manifest


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    create = subparsers.add_parser("prepare")
    create.add_argument("--runtime", type=Path, required=True)
    create.add_argument("--evidence", type=Path, action="append", required=True)
    create.add_argument("--output", type=Path, required=True)
    create.add_argument("--channel", default=CHANNEL)
    check = subparsers.add_parser("verify")
    check.add_argument("directory", type=Path)
    check.add_argument("--expected-product", default=PRODUCT_ID)
    check.add_argument("--expected-channel", default=CHANNEL)
    check.add_argument("--expected-commit")
    check.add_argument("--expected-rid")
    check.add_argument("--expected-version")
    args = parser.parse_args(argv)
    repo = Path(__file__).resolve().parents[1]
    previous = os.umask(0o077)
    try:
        if args.command == "prepare":
            prepare(args.runtime, args.evidence, args.output, repo, args.channel)
        else:
            verify(args.directory, repo, expected_product=args.expected_product, expected_channel=args.expected_channel,
                   expected_commit=args.expected_commit, expected_rid=args.expected_rid, expected_version=args.expected_version)
        print("Local release candidate verified; publication and built-in updates remain disabled.")
        return 0
    except (Exception, KeyboardInterrupt) as error:
        print("Release candidate rejected: " + (error.code if isinstance(error, ValidationFailure) else "release_validation_failed"))
        return 1
    finally:
        os.umask(previous)
