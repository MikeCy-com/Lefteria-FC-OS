import base64
import contextlib
import importlib.util
import io
import ipaddress
import json
import os
from pathlib import Path, PurePosixPath
import stat
import re
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location("prepare_environment", Path(__file__).with_name("prepare-environment.py"))
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)


def image_manifest():
    sha = "a" * 40
    return {"schema_version": 1, "repository": prepare.REPOSITORY, "source_sha": sha, "platform": "linux/amd64",
            "images": {name: {"reference": f"lefteria-fc-os-{name}:{sha}", "id": "sha256:" + character * 64,
                              "archive": f"lefteria-fc-os-{name}.tar.gz"}
                       for name, character in (("api", "b"), ("web", "c"), ("import", "d"))}}


def metadata(image_id, sha="a" * 40):
    return {"id": image_id, "os": "linux", "architecture": "amd64", "digests": [],
            "labels": {"org.opencontainers.image.revision": sha,
                       "org.opencontainers.image.source": f"https://github.com/{prepare.REPOSITORY}"}}


class PreviewEnvironmentChecks(unittest.TestCase):
    def test_only_explicit_paths_inside_the_dedicated_preview_are_accepted(self):
        with patch.object(prepare, "Path", PurePosixPath), patch.object(prepare, "ROOT", PurePosixPath("/srv/lefteria-os-preview")):
            root = str(prepare.ROOT)
            self.assertEqual(prepare.ROOT / "config", prepare.lexical_path(root + "/config"))
            for value in ("relative/config", "/srv/production/config", root + "/../config", root + "/config/",
                          root + "//config", root + "/./config"):
                with self.subTest(value=value), self.assertRaises(prepare.PreparationRefused):
                    prepare.lexical_path(value)
        with patch.object(prepare, "protected_chain"), patch.object(prepare, "protected_file"), \
                patch.object(Path, "lstat", side_effect=FileNotFoundError):
            for stamp in ("20261031t174927z", "20261031T174927Z"):
                prepare.validate_paths(prepare.ROOT / "bundle/manifest.json", prepare.ROOT / "config",
                                       prepare.ROOT / "manifests" / stamp, prepare.ROOT / "tls")
            for manifests, certificates, output in (
                (prepare.ROOT / "manifests", prepare.ROOT / "tls", prepare.ROOT / "config"),
                (prepare.ROOT / "manifests/production", prepare.ROOT / "tls", prepare.ROOT / "config"),
                (prepare.ROOT / "manifests/20261031t174927z", Path("/etc/letsencrypt"), prepare.ROOT / "config"),
                (prepare.ROOT / "manifests/20261031t174927z", prepare.ROOT / "tls", prepare.ROOT / "other"),
            ):
                with self.subTest(manifests=manifests), self.assertRaises(prepare.PreparationRefused):
                    prepare.validate_paths(prepare.ROOT / "bundle/manifest.json", output, manifests, certificates)

    def test_directory_and_file_guards_reject_symlinks_permissions_owners_and_hardlinks(self):
        directory = SimpleNamespace(st_mode=stat.S_IFDIR | 0o700, st_uid=0)
        self.assertTrue(prepare.directory_info_safe(directory, private=True))
        for mode, user in ((stat.S_IFLNK | 0o700, 0), (stat.S_IFDIR | 0o777, 0),
                           (stat.S_IFDIR | 0o700, 1000), (stat.S_IFDIR | 0o755, 0)):
            self.assertFalse(prepare.directory_info_safe(SimpleNamespace(st_mode=mode, st_uid=user), private=True))
        file = dict(st_mode=stat.S_IFREG | 0o600, st_uid=0, st_nlink=1, st_size=3)
        self.assertTrue(prepare.file_info_safe(SimpleNamespace(**file), 10))
        for changed in ({"st_mode": stat.S_IFLNK | 0o600}, {"st_mode": stat.S_IFREG | 0o644},
                        {"st_uid": 1000}, {"st_nlink": 2}, {"st_size": 0}, {"st_size": 11}):
            self.assertFalse(prepare.file_info_safe(SimpleNamespace(**{**file, **changed}), 10))
        with patch.object(Path, "lstat", return_value=SimpleNamespace(st_mode=stat.S_IFLNK | 0o700, st_uid=0)):
            with self.assertRaises(prepare.PreparationRefused):
                prepare.protected_chain(prepare.ROOT)

    def test_manifest_requires_exact_repository_revision_platform_and_three_distinct_images(self):
        value = image_manifest()
        self.assertIs(value, prepare.validate_manifest(value, "a" * 40))
        for changed in ({"schema_version": True}, {"repository": "other/repository"}, {"source_sha": "e" * 40},
                        {"platform": "linux/arm64"}, {"unknown": "ignored"}, {"images": {}}):
            with self.subTest(changed=changed), self.assertRaises(prepare.PreparationRefused):
                prepare.validate_manifest({**value, **changed}, "a" * 40)
        for changed in ({"reference": "production:latest"}, {"id": "sha256:short"},
                        {"archive": "../archive"}, {"unknown": "ignored"}):
            candidate = image_manifest()
            candidate["images"]["api"].update(changed)
            with self.subTest(changed=changed), self.assertRaises(prepare.PreparationRefused):
                prepare.validate_manifest(candidate, "a" * 40)
        value["images"]["web"]["id"] = value["images"]["api"]["id"]
        with self.assertRaises(prepare.PreparationRefused):
            prepare.validate_manifest(value, "a" * 40)
        with self.assertRaises(prepare.PreparationRefused):
            prepare.unique_object([("source_sha", "a"), ("source_sha", "b")])

    def test_loaded_images_must_match_exact_immutable_identity_and_labels(self):
        manifest = image_manifest()
        valid = [metadata(value["id"]) for value in manifest["images"].values()]
        with patch.object(prepare, "inspect_image", side_effect=valid) as inspect:
            result = prepare.validate_loaded_images(manifest)
        self.assertEqual({name: value["id"] for name, value in manifest["images"].items()}, result)
        self.assertEqual([value["reference"] for value in manifest["images"].values()],
                         [call.args[0] for call in inspect.call_args_list])
        for changed in ({"id": "sha256:" + "f" * 64}, {"os": "windows"}, {"architecture": "arm64"},
                        {"labels": None}, {"labels": {"org.opencontainers.image.revision": "e" * 40}}):
            with self.subTest(changed=changed), patch.object(prepare, "inspect_image", return_value={**valid[0], **changed}):
                with self.assertRaisesRegex(prepare.PreparationRefused, "loaded_image_provenance_mismatch"):
                    prepare.validate_loaded_images(manifest)

    def test_each_secret_has_independent_entropy_and_required_encoding(self):
        with patch.object(prepare.secrets, "token_bytes", side_effect=[bytes([value]) * 32 for value in range(1, 8)]) as draw:
            values = prepare.fresh_secrets()
        self.assertEqual(7, draw.call_count)
        self.assertTrue(all(call.args == (32,) for call in draw.call_args_list))
        decoded = []
        for name, value in values.items():
            decoded.append(base64.b64decode(value) if name in ("signing", "otp", "admin_throttle") else base64.urlsafe_b64decode(value + "="))
        self.assertEqual(7, len(set(decoded)))
        self.assertTrue(all(len(value) == 32 for value in decoded))
        with patch.object(prepare.secrets, "token_bytes", return_value=b"x" * 32):
            with self.assertRaisesRegex(prepare.PreparationRefused, "independent_entropy_required"):
                prepare.fresh_secrets()

    def test_bcrypt_uses_only_stdin_in_bounded_non_network_container_without_ambient_secrets(self):
        image_id = "sha256:" + "e" * 64
        actual = {**metadata(image_id), "digests": ["caddy@sha256:" + "f" * 64]}
        password = "PRIVATE_FICTIONAL_GATE_VALUE"
        hashed = "$2a$14$" + "a" * 53
        with patch.object(prepare, "inspect_image", return_value=actual), \
                patch.object(prepare, "docker", return_value=(hashed + "\n").encode()) as docker:
            self.assertEqual(hashed, prepare.gate_hash(password))
        arguments, content = docker.call_args.args
        self.assertEqual(password.encode() + b"\n", content)
        self.assertNotIn(password, " ".join(arguments))
        self.assertNotIn("--plaintext", arguments)
        self.assertNotIn("--stdin", arguments)
        self.assertIn("--pull=never", arguments)
        self.assertEqual("none", arguments[arguments.index("--network") + 1])
        self.assertIn("--read-only", arguments)
        self.assertEqual("NET_BIND_SERVICE", arguments[arguments.index("--cap-add") + 1])
        self.assertIn(image_id, arguments)
        with patch.object(prepare.subprocess, "run", return_value=SimpleNamespace(returncode=0, stdout=b"{}")) as run, \
                patch.dict(os.environ, {"DOCKER_HOST": "tcp://not-local:2375", "PRIVATE_SECRET": password}):
            prepare.docker(["image", "inspect", "fictional"])
        self.assertEqual(prepare.DOCKER, run.call_args.args[0][:3])
        self.assertNotIn("PRIVATE_SECRET", run.call_args.kwargs["env"])
        self.assertNotIn("DOCKER_HOST", run.call_args.kwargs["env"])
        for changed in ({"digests": ["untrusted/caddy@sha256:" + "f" * 64]}, {"architecture": "arm64"}):
            with patch.object(prepare, "inspect_image", return_value={**actual, **changed}), \
                    patch.object(prepare, "docker") as docker:
                with self.assertRaises(prepare.PreparationRefused):
                    prepare.gate_hash(password)
                docker.assert_not_called()

    def test_dotenv_preserves_bcrypt_dollars_and_client_file_contains_only_gate_access(self):
        credentials = {"owner": "owner-only", "application": "app-only", "signing": "signing-only",
                       "otp": "otp-only", "gate": "gate-only", "admin_password": "admin-only", "admin_throttle": "admin-key-only"}
        hashed = "$2a$14$" + "a" * 53
        images = {name: value["id"] for name, value in image_manifest()["images"].items()}
        files = prepare.configuration_files("a" * 40, images, PurePosixPath("/srv/lefteria-os-preview/manifests/20261031t174927z"),
                                            PurePosixPath("/srv/lefteria-os-preview/tls"), credentials, hashed)
        environment = files[".env"].decode()
        self.assertIn("PREVIEW_GATE_HASH='" + hashed + "'\n", environment)
        self.assertIn("PREVIEW_BIND='127.0.0.1'\n", environment)
        self.assertIn("PREVIEW_BIND_IPV6='::1'\n", environment)
        self.assertIn("PREVIEW_WEB_ADMIN_ENABLED='false'\n", environment)
        self.assertIn("PREVIEW_ADMIN_AUTH_PASSWORD='admin-only'\n", environment)
        self.assertIn("PREVIEW_API_IMAGE='" + images["api"] + "'\n", environment)
        self.assertNotIn(credentials["gate"], environment)
        access = json.loads(files["client-access.json"])
        self.assertEqual({"url": f"https://{prepare.HOSTNAME}:8443", "username": "preview", "password": "gate-only"}, access)
        for value in ("line\nbreak", "line\rbreak", "apost'rophe", "back\\slash", "nul\0", ""):
            with self.subTest(value=value), self.assertRaises(prepare.PreparationRefused):
                prepare.dotenv_value(value)

    def test_compose_and_example_keep_both_address_families_bound_to_loopback(self):
        directory = Path(__file__).parent
        compose = (directory / 'compose.yaml').read_text(encoding='utf-8')
        edge = compose.split('  edge:\n', 1)[1].split('\n  web:', 1)[0]
        self.assertIn('"${PREVIEW_BIND:-127.0.0.1}:8443:8443"', edge)
        self.assertIn('"[${PREVIEW_BIND_IPV6:-::1}]:8443:8443"', edge)
        # No private API/database/tool may acquire an independent host port.
        self.assertEqual(1, compose.count('\n    ports:'))
        values = dict(line.split('=', 1) for line in (directory / '.env.example').read_text(encoding='utf-8').splitlines()
                      if line and not line.startswith('#'))
        for variable, version in (('PREVIEW_BIND', 4), ('PREVIEW_BIND_IPV6', 6)):
            fallback = re.search(r'\$\{' + variable + r':-([^}]+)\}', edge)
            self.assertIsNotNone(fallback)
            address = ipaddress.ip_address(fallback.group(1))
            self.assertEqual(version, address.version)
            self.assertTrue(address.is_loopback)
            self.assertEqual(str(address), values[variable])

    def test_errors_never_log_lower_level_output_or_secret_material(self):
        arguments = ["--source-sha", "a" * 40, "--bundle-manifest", str(prepare.ROOT / "bundle/manifest.json"),
                     "--output-directory", str(prepare.ROOT / "config"), "--manifests-directory", str(prepare.ROOT / "manifests/20261031t174927z"),
                     "--certificate-directory", str(prepare.ROOT / "tls")]
        output = io.StringIO()
        with patch.object(prepare, "prepare", side_effect=RuntimeError("PRIVATE_FICTIONAL_SECRET")), \
                contextlib.redirect_stderr(output):
            self.assertEqual(1, prepare.main(arguments))
        self.assertEqual("Preview environment preparation refused: guarded_operation_failed\n", output.getvalue())
        output = io.StringIO()
        with contextlib.redirect_stderr(output):
            self.assertEqual(1, prepare.main([*arguments, "--unexpected", "PRIVATE_FICTIONAL_SECRET"]))
        self.assertEqual("Preview environment preparation refused: invalid_arguments\n", output.getvalue())


@unittest.skipUnless(sys.platform == "linux" and hasattr(os, "geteuid") and os.geteuid() == 0,
                     "Actual private filesystem/renameat2 checks require Linux root")
class PreviewEnvironmentFilesystemChecks(unittest.TestCase):
    def test_atomic_publication_sets_private_modes_and_never_replaces_existing_output(self):
        with tempfile.TemporaryDirectory(prefix="preview-environment-test-", dir="/root") as temporary:
            root = Path(temporary)
            files = {".env": b"PRIVATE_FICTIONAL_ENV\n", "client-access.json": b'{"password":"fictional"}\n'}
            with patch.object(prepare, "ROOT", root):
                prepare.write_configuration(root / "config", files)
                self.assertEqual(0o700, stat.S_IMODE((root / "config").stat().st_mode))
                for name, expected in files.items():
                    self.assertEqual(expected, (root / "config" / name).read_bytes())
                    self.assertEqual(0o600, stat.S_IMODE((root / "config" / name).stat().st_mode))
                with self.assertRaisesRegex(prepare.PreparationRefused, "output_already_exists"):
                    prepare.write_configuration(root / "config", {name: b"replacement" for name in files})
                self.assertEqual(files[".env"], (root / "config/.env").read_bytes())
                self.assertFalse(list(root.glob(".config.prepare-*")))

    def test_failed_publication_cleans_only_exact_created_files_and_keeps_unknown_entries(self):
        with tempfile.TemporaryDirectory(prefix="preview-environment-test-", dir="/root") as temporary:
            root = Path(temporary)
            with patch.object(prepare, "ROOT", root), patch.object(prepare, "publish_new_directory", side_effect=OSError("fictional")):
                with self.assertRaises(OSError):
                    prepare.write_configuration(root / "config", {".env": b"fictional", "client-access.json": b"fictional"})
                self.assertFalse(list(root.glob(".config.prepare-*")))
            unknown = root / "unknown"
            unknown.mkdir(mode=0o700)
            (unknown / "unowned.txt").write_bytes(b"must remain")
            info = unknown.stat()
            self.assertFalse(prepare.cleanup_created_directory(unknown, prepare.identity(info), {}))
            self.assertEqual(b"must remain", (unknown / "unowned.txt").read_bytes())


if __name__ == "__main__":
    unittest.main()
