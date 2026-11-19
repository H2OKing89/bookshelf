"""Read build provenance from a real owned application, never a live home."""

import json
from pathlib import Path
import re

from .app import Fixture, result, safe_failure
from .common import snapshot_runtime


PRODUCT_ID = "bookshelf-audiobooks"
SOURCE_FIELDS = ("productId", "sourceCommit", "sourceTree", "sourceBranch", "buildDirty")


def identity(status):
    return {name: status.get(name) for name in SOURCE_FIELDS}


def valid_identity(value):
    return (value.get("productId") == PRODUCT_ID
            and isinstance(value.get("sourceCommit"), str) and re.fullmatch(r"[0-9a-f]{40}", value["sourceCommit"])
            and isinstance(value.get("sourceTree"), str) and re.fullmatch(r"[0-9a-f]{40}", value["sourceTree"])
            and isinstance(value.get("sourceBranch"), str) and bool(value["sourceBranch"])
            and type(value.get("buildDirty")) is bool)


def run(runtime: Path, output: Path):
    fixture = Fixture(runtime, output)
    before = snapshot_runtime(runtime)
    completed, failure, metadata = False, None, {}
    try:
        frontend = json.loads((runtime / "UI/source-provenance.json").read_text())
        fixture.configure_home()
        fixture.start()
        status = fixture.api("system/status")
        source = identity(status)
        fixture.check("application_has_compiled_fork_source_identity", valid_identity(source))
        fixture.check("frontend_and_backend_share_the_source_identity", frontend == source)
        fixture.check("built_in_updates_are_disabled", status.get("builtInUpdatesAvailable") is False)
        fixture.check("compiled_version_is_explicit", bool(re.fullmatch(r"\d+\.\d+\.\d+\.\d+", status.get("version", ""))))
        metadata.update(source=source, version=status["version"])
        completed = True
    except Exception as error:
        fixture.checks["release_identity_completed"] = False
        failure = safe_failure("release_identity", error)
    finally:
        try:
            fixture.cleanup()
            fixture.checks["owned_process_cleaned_up"] = fixture.process is None
        except Exception:
            fixture.checks["owned_process_cleaned_up"] = False
        fixture.checks["package_unchanged"] = snapshot_runtime(runtime) == before
    return result(fixture, completed, metadata=metadata, failure=failure)
