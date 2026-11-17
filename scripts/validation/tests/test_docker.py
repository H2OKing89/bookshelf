import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from validation import docker
from validation.common import ValidationFailure


class DockerEndpointTests(unittest.TestCase):
    def test_remote_hosts_are_rejected_before_any_docker_command(self):
        for endpoint in ("ssh://synthetic.invalid", "tcp://127.0.0.1:2375"):
            with (self.subTest(endpoint=endpoint), patch.dict(os.environ, {"DOCKER_HOST": endpoint}, clear=True),
                  patch.object(docker, "_command") as command, self.assertRaises(ValidationFailure) as error):
                docker._local_docker_environment()
            self.assertEqual(error.exception.code, "local_docker_unix_socket_required")
            command.assert_not_called()

    def test_remote_context_cannot_override_a_local_host_environment(self):
        environment = {"DOCKER_CONTEXT": "synthetic-remote", "DOCKER_HOST": "unix:///tmp/synthetic-docker.sock"}
        response = subprocess.CompletedProcess([], 0, json.dumps("ssh://synthetic.invalid"), "")
        with (patch.dict(os.environ, environment, clear=True), patch.object(docker, "_command", return_value=response),
              self.assertRaises(ValidationFailure) as error):
            docker._local_docker_environment()
        self.assertEqual(error.exception.code, "local_docker_unix_socket_required")

    def test_local_context_is_pinned_for_all_child_commands(self):
        endpoint = "unix:///tmp/synthetic-docker.sock"
        response = subprocess.CompletedProcess([], 0, json.dumps(endpoint), "")
        with patch.dict(os.environ, {"DOCKER_CONTEXT": "synthetic-local"}, clear=True), patch.object(docker, "_command", return_value=response):
            environment = docker._local_docker_environment()
        self.assertEqual(environment["DOCKER_HOST"], endpoint)
        self.assertNotIn("DOCKER_CONTEXT", environment)


class DockerStateTests(unittest.TestCase):
    def test_only_explicit_missing_requested_container_is_absent(self):
        for message in ("Error: No such container: owned-container\n",
                        "Error response from daemon: No such object: owned-container\n"):
            response = subprocess.CompletedProcess([], 1, "", message)
            with self.subTest(message=message), patch.object(docker, "_command", return_value=response):
                self.assertIsNone(docker._state("owned-container", {}))

    def test_daemon_permission_and_missing_socket_failures_are_not_absence(self):
        messages = ("Cannot connect to the Docker daemon at unix:///synthetic.sock",
                    "permission denied while trying to connect to the Docker daemon socket",
                    "dial unix /synthetic.sock: connect: no such file or directory",
                    "Error: No such container: a-different-container\n")
        for message in messages:
            response = subprocess.CompletedProcess([], 1, "", message)
            with (self.subTest(message=message), patch.object(docker, "_command", return_value=response),
                  self.assertRaises(ValidationFailure) as error):
                docker._state("owned-container", {})
            self.assertEqual(error.exception.code, "docker_container_inspection_failed")
            self.assertNotIn(message, str(error.exception))

    def test_success_with_invalid_state_is_not_absence(self):
        for body in ("null", "{}", "[]", "synthetic-invalid-state"):
            response = subprocess.CompletedProcess([], 0, body, "")
            with (self.subTest(body=body), patch.object(docker, "_command", return_value=response),
                  self.assertRaises(ValidationFailure) as error):
                docker._state("owned-container", {})
            self.assertEqual(error.exception.code, "docker_container_state_invalid")


class DockerCleanupTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.runtime = self.root / "runtime"
        self.runtime.mkdir()
        (self.runtime / "UI").mkdir()
        for name in ("Readarr.Core.dll", "UI/index.html"):
            (self.runtime / name).write_bytes(b"synthetic package")
        elf = bytearray(64)
        elf[:6] = b"\x7fELF\x02\x01"
        struct.pack_into("<H", elf, 18, 62)
        struct.pack_into("<H", elf, 54, 56)
        (self.runtime / "Readarr").write_bytes(elf)
        (self.runtime / "Readarr.runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
            "tfm": "net10.0", "includedFrameworks": [{"name": "Microsoft.NETCore.App", "version": "10.0.15"}]}}))
        (self.runtime / "Readarr.deps.json").write_text(json.dumps({
            "runtimeTarget": {"name": ".NETCoreApp,Version=v10.0/linux-musl-x64"}}))
        self.repo = self.root / "repo"
        (self.repo / "docker").mkdir(parents=True)
        (self.repo / "docker/Dockerfile").write_text("FROM scratch\n")
        (self.repo / "scripts/mediainfo-licenses").mkdir(parents=True)
        for name in ("build-mediainfo.sh", "mediainfo.lock"):
            (self.repo / "scripts" / name).write_text("synthetic build input\n")
        self.container = "a" * 64
        self.removals = []
        self.ownership_matches = True
        self.created = False
        self.interrupt_create = False
        self.interrupt_before_create = False
        self.unknown_cleanup_state = False

    def invoke_failed_start(self):
        def command(arguments, environment, timeout=30):
            if arguments[1] == "info":
                return subprocess.CompletedProcess(arguments, 0, "linux/amd64\n", "")
            if arguments[1:3] == ["image", "inspect"]:
                value = json.dumps([]) if any(".Config.Env" in value for value in arguments) else "sha256:" + "b" * 64
                return subprocess.CompletedProcess(arguments, 0, value, "")
            if arguments[1] == "create":
                if self.interrupt_before_create:
                    raise KeyboardInterrupt()
                self.created = True
                if self.interrupt_create:
                    raise KeyboardInterrupt()
                return subprocess.CompletedProcess(arguments, 0, self.container + "\n", "")
            if arguments[1] == "start":
                return subprocess.CompletedProcess(arguments, 1, "", "synthetic start failure")
            if arguments[1] == "rm":
                self.removals.append(arguments[-1])
                self.created = False
                return subprocess.CompletedProcess(arguments, 0, "", "")
            if arguments[1:3] == ["image", "rm"]:
                return subprocess.CompletedProcess(arguments, 0, "", "")
            raise AssertionError("Unexpected mocked Docker operation")

        completed = subprocess.CompletedProcess([], 0, "c" * 40 + "\n", "")
        def state(*arguments):
            if self.created and self.unknown_cleanup_state:
                raise ValidationFailure("docker_container_inspection_failed")
            return {"Running": False} if self.created else None
        with (patch.object(docker, "_local_docker_environment", return_value={}),
              patch.object(docker, "host_machine", return_value=62),
              patch.object(docker, "_command", side_effect=command),
              patch.object(docker, "_owned", return_value=self.ownership_matches),
              patch.object(docker, "_state", side_effect=state),
              patch.object(docker, "_store_logs", side_effect=OSError("synthetic-log-secret")),
              patch.object(docker.subprocess, "run", return_value=completed)):
            return docker.run(self.runtime, self.root / "results", self.repo)

    def test_failed_start_and_log_collection_still_remove_only_the_owned_container(self):
        result = self.invoke_failed_start()
        self.assertEqual(self.removals, [self.container])
        self.assertTrue(result["checks"]["all_owned_docker_containers_removed"])
        self.assertFalse(result["checks"]["owned_container_0_logs_saved"])
        self.assertFalse(result["completed"])
        self.assertNotIn("synthetic-log-secret", json.dumps(result))

    def test_cleanup_refuses_a_container_whose_owner_label_changed(self):
        self.ownership_matches = False
        result = self.invoke_failed_start()
        self.assertFalse(self.removals)
        self.assertFalse(result["checks"]["all_owned_docker_containers_removed"])

    def test_interrupted_create_cleans_its_owned_name_before_an_id_is_returned(self):
        self.interrupt_create = True
        with self.assertRaises(KeyboardInterrupt):
            self.invoke_failed_start()
        self.assertEqual(len(self.removals), 1)
        self.assertRegex(self.removals[0], r"^bookshelf-validation-s6-[0-9a-f]{32}$")
        self.assertFalse(self.created)

    def test_interrupted_create_with_no_container_does_not_remove_anything(self):
        self.interrupt_before_create = True
        with self.assertRaises(KeyboardInterrupt):
            self.invoke_failed_start()
        self.assertFalse(self.removals)
        self.assertFalse(self.created)

    def test_unknown_cleanup_state_fails_instead_of_claiming_removal(self):
        self.unknown_cleanup_state = True
        result = self.invoke_failed_start()
        self.assertFalse(self.removals)
        self.assertFalse(result["checks"]["all_owned_docker_containers_removed"])
        self.assertGreater(result["failed"], 0)
        self.assertEqual(result["metadata"]["ownedContainerIdentifiers"], [self.container])


if __name__ == "__main__":
    unittest.main()
