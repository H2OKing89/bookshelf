from pathlib import Path
import json
import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock
from urllib.parse import unquote

from validation import common, media


def package(root):
    root.mkdir()
    (root / "UI").mkdir()
    for name in ("Readarr", "Readarr.dll", "Readarr.Core.dll", "UI/index.html"):
        (root / name).write_bytes(b"synthetic package fixture")
    (root / "Readarr.runtimeconfig.json").write_text(json.dumps({
        "runtimeOptions": {
            "tfm": "net10.0",
            "includedFrameworks": [{"name": "Microsoft.NETCore.App", "version": "10.0.15"}],
        },
    }))
    return root


def running(pid):
    try:
        fields = Path("/proc", str(pid), "stat").read_text().rpartition(")")[2].split()
        return fields[0] not in ("Z", "X")
    except (OSError, IndexError):
        return False


class MediaBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def test_readonly_entry_is_replaced_only_in_private_clone(self):
        runtime = package(self.root / "runtime")
        (runtime / "Readarr.dll").chmod(0o444)
        (runtime / "internal.dll").symlink_to("Readarr.Core.dll")
        before = common.snapshot_runtime(runtime)
        probe = self.root / media.PROBE_ASSEMBLY
        probe.write_bytes(b"synthetic compiled probe")

        clone = media.stage_clone(runtime, probe, self.root / "clone")

        self.assertEqual(common.snapshot_runtime(runtime), before)
        self.assertEqual((clone / "Readarr.dll").read_bytes(), probe.read_bytes())
        self.assertEqual((clone / "Readarr.Application.dll").read_bytes(), b"synthetic package fixture")
        self.assertEqual((clone / "Readarr.Core.dll").read_bytes(), b"synthetic package fixture")
        self.assertTrue((clone / "internal.dll").is_symlink())
        self.assertEqual(clone.stat().st_mode & 0o777, 0o700)

    def test_sdk_paths_remain_single_escaped_properties(self):
        runtime = package(self.root / "candidate; OutputPath=outside %3B $()")
        output = self.root / "output; BaseIntermediateOutputPath=outside %3B @()"
        observed = {}

        def execute(command, **kwargs):
            properties = {}
            for argument in command:
                if argument.startswith("-p:"):
                    name, value = argument[3:].split("=", 1)
                    # MSBuild parses semicolon-separated properties itself;
                    # a raw delimiter here could redirect SDK writes.
                    self.assertNotIn(";", value)
                    properties[name] = unquote(value)
            observed.update(properties)
            self.assertEqual(kwargs["cwd"], media.PROJECT.parents[3])
            Path(properties["OutputPath"], media.PROBE_ASSEMBLY).write_bytes(b"synthetic compiled probe")
            return {"exitCode": 0, "timedOut": False}

        with mock.patch.object(media, "_execute", side_effect=execute):
            probe = media.build_probe(runtime, output)

        self.assertEqual(observed["CandidateDir"], str(runtime))
        self.assertEqual(observed["ValidationOutputDir"], str(output))
        self.assertEqual(Path(observed["OutputPath"]), output / "bin")
        self.assertEqual(Path(observed["BaseIntermediateOutputPath"]), output / "obj")
        self.assertEqual(probe, output / "bin" / media.PROBE_ASSEMBLY)
        self.assertFalse((self.root / "outside").exists())


@unittest.skipUnless(hasattr(os, "WNOWAIT") and Path("/proc").is_dir(), "Linux process tracking")
class MediaProcessTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)

    def test_timeout_stops_owned_process(self):
        pid_file = self.root / "owned.pid"
        command = [sys.executable, "-c",
                   "import os,time,pathlib,sys; pathlib.Path(sys.argv[1]).write_text(str(os.getpid())); time.sleep(60)",
                   str(pid_file)]
        started = time.monotonic()

        result = media._execute(command, cwd=self.root, log=self.root / "timeout.log", timeout=1)

        self.assertTrue(result["timedOut"])
        self.assertLess(result["exitCode"], 0)
        self.assertLess(time.monotonic() - started, 7)
        self.assertFalse(running(int(pid_file.read_text())))

    def test_exited_leader_does_not_leave_running_grandchild_or_kill_unrelated_group(self):
        unrelated = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(60)"],
                                     start_new_session=True, stdout=subprocess.DEVNULL,
                                     stderr=subprocess.DEVNULL)
        self.addCleanup(self.stop_unrelated, unrelated)
        child_file = self.root / "grandchild.pid"
        command = [sys.executable, "-c", (
            "import os,pathlib,subprocess,sys; "
            "child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(60)']); "
            "pathlib.Path(sys.argv[1]).write_text(str(child.pid)); os._exit(0)"
        ), str(child_file)]

        result = media._execute(command, cwd=self.root, log=self.root / "grandchild.log", timeout=5)

        self.assertEqual(result, {"exitCode": 0, "timedOut": False})
        self.assertFalse(running(int(child_file.read_text())))
        self.assertIsNone(unrelated.poll())

    def test_interrupt_propagates_after_owned_child_cleanup(self):
        child_file = self.root / "interrupt.pid"
        script = """import sys
from pathlib import Path
sys.path.insert(0, sys.argv[2])
from validation import media
root = Path(sys.argv[1])
command = [sys.executable, '-c',
           'import os,pathlib,sys,time; pathlib.Path(sys.argv[1]).write_text(str(os.getpid())); time.sleep(60)',
           str(root / 'interrupt.pid')]
try:
    media._execute(command, cwd=root, log=root / 'interrupt.log', timeout=60)
except KeyboardInterrupt:
    raise SystemExit(130)
"""
        coordinator = subprocess.Popen(
            [sys.executable, "-c", script, str(self.root), str(media.PROJECT.parents[2])],
            start_new_session=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        )
        self.addCleanup(self.stop_unrelated, coordinator)
        deadline = time.monotonic() + 5
        while not child_file.is_file():
            self.assertLess(time.monotonic(), deadline)
            self.assertIsNone(coordinator.poll())
            time.sleep(0.01)
        coordinator.send_signal(signal.SIGINT)
        self.assertEqual(coordinator.wait(timeout=8), 130)
        self.assertFalse(running(int(child_file.read_text())))

    @staticmethod
    def stop_unrelated(process):
        if process.poll() is None:
            process.terminate()
            process.wait(timeout=5)
