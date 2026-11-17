"""Shared boundaries for local checks; importing this module starts no processes."""

import hashlib
import json
import os
from pathlib import Path
import platform
import re
import stat
import struct
import tempfile


class ValidationFailure(Exception):
    """Only a stable identifier may reach console output or result files."""

    def __init__(self, code):
        self.code = code if re.fullmatch(r"[a-z][a-z0-9_]*", code) else "validation_failed"
        super().__init__(self.code)


def resolve_runtime(runtime):
    root = Path(runtime).expanduser().resolve(strict=True)
    if not root.is_dir() or not all((root / item).is_file() for item in
                                   ("Readarr", "Readarr.Core.dll", "Readarr.runtimeconfig.json", "UI/index.html")):
        raise ValidationFailure("complete_linux_package_required")
    for directory, _, files in os.walk(root, followlinks=False):
        if {"config.xml", "readarr.db", "cache.db", "logs.db"}.intersection(files):
            raise ValidationFailure("runtime_must_not_contain_an_application_home")
    return root


def validate_output(runtime, output):
    runtime = resolve_runtime(runtime)
    output = Path(output).expanduser().resolve()
    if output.is_relative_to(runtime) or runtime.is_relative_to(output):
        raise ValidationFailure("runtime_and_output_must_be_disjoint")
    # Existing application homes are never valid output parents.
    for ancestor in (output, *output.parents):
        if any((ancestor / item).is_file() for item in ("readarr.db", "config.xml")):
            raise ValidationFailure("output_must_not_use_an_application_home")
    return runtime, output


def private_directory(path):
    path = Path(path)
    if path.is_symlink() or path.exists():
        raise ValidationFailure("new_output_directory_required")
    path.mkdir(mode=0o700)
    path.chmod(0o700)
    return path


def file_sha256(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def snapshot_runtime(runtime):
    root = resolve_runtime(runtime)
    result = {}
    for directory, directories, files in os.walk(root, followlinks=False):
        for name in sorted(directories + files):
            path = Path(directory) / name
            relative = str(path.relative_to(root))
            if path.is_symlink():
                target = path.resolve(strict=True)
                if not target.is_relative_to(root):
                    raise ValidationFailure("runtime_symlink_leaves_package")
                result[relative] = "link:" + os.readlink(path)
            elif path.is_file():
                result[relative] = file_sha256(path) + ":" + oct(stat.S_IMODE(path.stat().st_mode))
    return result


def runtime_fingerprint(snapshot):
    return hashlib.sha256(json.dumps(snapshot, sort_keys=True, separators=(",", ":")).encode()).hexdigest()


def runtime_frameworks(runtime):
    data = json.loads((Path(runtime) / "Readarr.runtimeconfig.json").read_text())
    options = data["runtimeOptions"]
    frameworks = options.get("includedFrameworks", [])
    if not frameworks or options.get("tfm") != "net10.0":
        raise ValidationFailure("self_contained_net10_package_required")
    return {item["name"]: item["version"] for item in frameworks}


def ensure_host_compatible(runtime):
    """Never try a foreign apphost or silently invoke an emulator."""
    if platform.system() != "Linux":
        raise ValidationFailure("linux_host_required")
    machine = {"x86_64": 62, "amd64": 62, "aarch64": 183, "arm64": 183}.get(platform.machine().lower())
    raw = (Path(runtime) / "Readarr").read_bytes()
    if len(raw) < 64 or raw[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<H", raw, 18)[0] != machine:
        raise ValidationFailure("native_host_architecture_required")
    offset = struct.unpack_from("<Q", raw, 32)[0]
    size, count = struct.unpack_from("<HH", raw, 54)
    if size < 56 or offset + size * count > len(raw):
        raise ValidationFailure("invalid_apphost_elf")
    for index in range(count):
        entry = offset + index * size
        if struct.unpack_from("<I", raw, entry)[0] == 3:
            start = struct.unpack_from("<Q", raw, entry + 8)[0]
            length = struct.unpack_from("<Q", raw, entry + 32)[0]
            if start + length > len(raw):
                raise ValidationFailure("invalid_apphost_elf")
            interpreter = raw[start:start + length].rstrip(b"\0").decode("ascii")
            if not Path(interpreter).is_file():
                raise ValidationFailure("native_runtime_loader_missing_use_matching_container")


def write_json(path, value):
    """Private atomic reports; credentials must never be part of value."""
    path = Path(path)
    descriptor, temporary = tempfile.mkstemp(prefix=".validation-", dir=path.parent)
    try:
        with os.fdopen(descriptor, "w") as stream:
            json.dump(value, stream, indent=2)
            stream.write("\n")
        os.replace(temporary, path)
    finally:
        Path(temporary).unlink(missing_ok=True)


def failure_result(code, error=None):
    result = {"checks": {code: False}, "passed": 0, "failed": 1, "completed": False}
    if error is not None:
        result["exceptionType"] = type(error).__name__
    return result
