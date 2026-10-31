import contextlib
import io
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
import _acme_hook as hook


class PreviewAcmeHookChecks(unittest.TestCase):
    def setUp(self):
        self.token = "t" * 43
        self.validation = self.token + "." + "v" * 43
        self.environment = {"CERTBOT_DOMAIN": hook.HOSTNAME, "CERTBOT_TOKEN": self.token,
                            "CERTBOT_VALIDATION": self.validation}
        self.guard = {"schema_version": 1, "hostname": hook.HOSTNAME,
                      "frontend_container_id": "a" * 64, "nginx_config_sha256": "b" * 64}

    def test_exact_hostname_and_http_key_authorization_are_required(self):
        self.assertEqual((self.token, self.validation.encode("ascii")), hook.validate_challenge(self.environment))
        for domain in ("lefteriafc.cy", "www.lefteriafc.cy", "other.hstgr.cloud", "*.hstgr.cloud", ""):
            with self.subTest(domain=domain), self.assertRaises(hook.HookRefused):
                hook.validate_challenge({**self.environment, "CERTBOT_DOMAIN": domain})
        for token in ("../escape", "x" * 257, "space value", "t" * 43 + "\n", "$(command)"):
            with self.subTest(token=token), self.assertRaises(hook.HookRefused):
                hook.validate_challenge({**self.environment, "CERTBOT_TOKEN": token})
        for validation in ("wrong." + "v" * 43, self.validation + "\n", self.validation + " ", self.token + ".short"):
            with self.subTest(validation=validation), self.assertRaises(hook.HookRefused):
                hook.validate_challenge({**self.environment, "CERTBOT_VALIDATION": validation})

    def test_guard_is_explicit_and_cannot_select_other_production_resources(self):
        self.assertEqual(self.guard, hook.validate_guard(self.guard))
        for changed in ({**self.guard, "hostname": "lefteriafc.cy"},
                        {**self.guard, "schema_version": True},
                        {**self.guard, "frontend_container_id": "lefteria-backend"},
                        {**self.guard, "nginx_config_sha256": "unknown"},
                        {**self.guard, "unexpected": "ignored"}):
            with self.subTest(changed=changed), self.assertRaises(hook.HookRefused):
                hook.validate_guard(changed)
        with self.assertRaises(hook.HookRefused):
            hook.unique_object([("key", 1), ("key", 2)])

    def test_container_or_configuration_changes_refuse_before_any_write(self):
        with patch.object(hook, "docker", return_value=b"changed|true|/lefteria-frontend\n") as docker:
            with self.assertRaisesRegex(hook.HookRefused, "production_frontend_changed"):
                hook.verify_frontend(self.guard)
            self.assertEqual(1, docker.call_count)
            self.assertEqual("inspect", docker.call_args.args[0][0])
        with patch.object(hook, "docker", side_effect=[
            (self.guard["frontend_container_id"] + "|true|/lefteria-frontend\n").encode(), b"changed config"]):
            with self.assertRaisesRegex(hook.HookRefused, "production_nginx_configuration_changed"):
                hook.verify_frontend(self.guard)

    def test_cleanup_never_deletes_a_file_without_exact_created_ownership(self):
        for actual in (hook.ownership(self.guard, self.token, self.validation.encode(), False),
                       {**hook.ownership(self.guard, self.token, self.validation.encode(), True), "token": "other"},
                       {**hook.ownership(self.guard, self.token, self.validation.encode(), True), "validation_sha256": "c" * 64}):
            with self.subTest(actual=actual), patch.dict(hook.os.environ, self.environment, clear=True), \
                    patch.object(hook, "load_guard", return_value=(Path("/guard.json"), self.guard)), \
                    patch.object(hook, "verify_frontend"), \
                    patch.object(hook, "state_path", return_value=Path("/owned.json")), \
                    patch.object(hook, "private_json", return_value=actual), \
                    patch.object(hook, "token_command") as token_command, \
                    patch.object(hook, "discard_owned_state") as discard:
                with self.assertRaisesRegex(hook.HookRefused, "created_token_ownership_required"):
                    hook.cleanup()
                token_command.assert_not_called()
                discard.assert_not_called()

    def test_cleanup_uses_stdin_bytes_and_exact_previously_owned_token(self):
        state = hook.ownership(self.guard, self.token, self.validation.encode(), True)
        with patch.dict(hook.os.environ, self.environment, clear=True), \
                patch.object(hook, "load_guard", return_value=(Path("/guard.json"), self.guard)), \
                patch.object(hook, "verify_frontend") as verify, \
                patch.object(hook, "state_path", return_value=Path("/owned.json")), \
                patch.object(hook, "private_json", return_value=state), \
                patch.object(hook, "token_command") as token_command, \
                patch.object(hook, "discard_owned_state") as discard:
            hook.cleanup()
            verify.assert_called_once_with(self.guard)
            token_command.assert_called_once_with(self.guard, self.token, self.validation.encode(), hook.REMOVE_TOKEN)
            discard.assert_called_once_with(Path("/owned.json"), state)
        with patch.object(hook, "docker") as docker:
            hook.token_command(self.guard, self.token, self.validation.encode(), hook.REMOVE_TOKEN)
            arguments, content = docker.call_args.args
            self.assertEqual(["preview-acme", self.token], arguments[-2:])
            self.assertEqual(self.validation.encode(), content)
            self.assertNotIn(self.validation, " ".join(arguments))

    def test_unexpected_failures_and_environment_values_are_not_logged(self):
        output = io.StringIO()
        with patch.object(hook, "authenticate", side_effect=RuntimeError("PRIVATE_TEST_VALUE")), contextlib.redirect_stderr(output):
            self.assertEqual(1, hook.main("auth"))
        self.assertEqual("Preview ACME hook refused: guarded_operation_failed\n", output.getvalue())
        self.assertNotIn("PRIVATE_TEST_VALUE", output.getvalue())


if __name__ == "__main__":
    unittest.main()
