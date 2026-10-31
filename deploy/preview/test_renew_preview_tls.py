import contextlib
import importlib.util
import io
import os
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('preview_tls_renewal', Path(__file__).with_name('renew-preview-tls.py'))
renewal = importlib.util.module_from_spec(spec)
spec.loader.exec_module(renewal)


class PreviewTlsRenewalChecks(unittest.TestCase):
    def setUp(self):
        self.identifier = 'a' * 64
        self.record = '|'.join((self.identifier, 'true', 'false', 'false', renewal.PROJECT, renewal.SERVICE)) + '\n'

    def test_exact_compose_labels_and_running_identity_are_rechecked(self):
        with patch.object(renewal, 'docker', side_effect=[(self.identifier + '\n').encode(), self.record.encode()]) as docker:
            self.assertEqual(self.identifier, renewal.verified_edge())
            query = docker.call_args_list[0].args
            self.assertIn('label=com.docker.compose.project=lefteria-os-preview', query)
            self.assertIn('label=com.docker.compose.service=edge', query)
            self.assertIn('status=running', query)
            self.assertIn('--no-trunc', query)
            self.assertEqual(self.identifier, docker.call_args_list[1].args[-1])
        for record in (self.record.replace('lefteria-os-preview', 'lefteriafc'),
                       self.record.replace('|edge', '|api'), self.record.replace('|true|', '|false|'),
                       self.record.replace('|false|false|', '|true|false|'),
                       self.record.replace('|false|false|', '|false|true|'), self.record.replace('a' * 64, 'b' * 64)):
            with self.subTest(record=record), patch.object(renewal, 'docker', side_effect=[(self.identifier + '\n').encode(), record.encode()]):
                with self.assertRaisesRegex(renewal.RenewalRefused, 'preview_edge_identity_changed'):
                    renewal.verified_edge()

    def test_missing_multiple_or_malformed_edges_never_reach_exec(self):
        for value, reason in ((b'', 'not_running'), ((self.identifier + '\n' + 'b' * 64 + '\n').encode(), 'ambiguous'),
                              (b'lefteria-frontend\n', 'ambiguous'), (b'abc\n', 'ambiguous')):
            with self.subTest(value=value), patch.object(renewal, 'docker', return_value=value) as docker:
                with self.assertRaisesRegex(renewal.RenewalRefused, reason):
                    renewal.verified_edge()
                self.assertEqual(1, docker.call_count)

    def test_publisher_precedes_only_the_verified_edge_reload(self):
        sequence = []
        with patch.object(renewal, 'verify_publisher'), \
                patch.object(renewal, 'run_checked', side_effect=lambda *args, **kwargs: sequence.append(('publish', args, kwargs))), \
                patch.object(renewal, 'verified_edge', side_effect=lambda: sequence.append(('verify',)) or self.identifier), \
                patch.object(renewal, 'docker', side_effect=lambda *args: sequence.append(('reload', args))):
            renewal.renew()
        self.assertEqual(['publish', 'verify', 'reload'], [entry[0] for entry in sequence])
        self.assertEqual([renewal.PYTHON, str(renewal.PUBLISHER)], sequence[0][1][0])
        self.assertEqual(('exec', self.identifier, 'caddy', 'reload', '--config', '/etc/caddy/Caddyfile', '--adapter', 'caddyfile'), sequence[2][1])
        with patch.object(renewal, 'verify_publisher'), \
                patch.object(renewal, 'run_checked', side_effect=renewal.RenewalRefused('publisher_failed')), \
                patch.object(renewal, 'verified_edge') as verify, patch.object(renewal, 'docker') as docker:
            with self.assertRaises(renewal.RenewalRefused):
                renewal.renew()
            verify.assert_not_called()
            docker.assert_not_called()

    def test_local_daemon_and_clean_environment_ignore_ambient_provider_or_docker_secrets(self):
        result = SimpleNamespace(returncode=0, stdout=b'ok', stderr=b'')
        with patch.dict(os.environ, {'DOCKER_HOST': 'tcp://wrong.invalid', 'DOCKER_CONTEXT': 'production',
                                     'TWILIO_AUTH_TOKEN': 'PRIVATE_TEST_VALUE', 'SSL_CERT_FILE': '/wrong'}, clear=False), \
                patch.object(renewal.subprocess, 'run', return_value=result) as run:
            self.assertEqual(b'ok', renewal.docker('ps'))
            self.assertEqual([renewal.DOCKER, '--host', 'unix:///var/run/docker.sock', 'ps'], run.call_args.args[0])
            environment = run.call_args.kwargs['env']
            self.assertEqual(renewal.SAFE_ENV, environment)
            self.assertNotIn('DOCKER_HOST', environment)
            self.assertNotIn('DOCKER_CONTEXT', environment)
            self.assertNotIn('TWILIO_AUTH_TOKEN', environment)
            self.assertNotIn('SSL_CERT_FILE', environment)

    def test_failure_output_contains_no_lower_level_secret_material(self):
        output = io.StringIO()
        with patch.object(sys, 'argv', ['renew-preview-tls.py']), \
                patch.object(renewal, 'renew', side_effect=RuntimeError('PRIVATE_TEST_VALUE')), contextlib.redirect_stderr(output):
            self.assertEqual(1, renewal.main())
        self.assertEqual('Preview TLS renewal refused: guarded_operation_failed\n', output.getvalue())

    def test_timer_and_service_target_only_the_isolated_certificate(self):
        directory = Path(__file__).parent
        service = (directory / 'lefteria-os-preview-renew.service').read_text(encoding='utf-8')
        timer = (directory / 'lefteria-os-preview-renew.timer').read_text(encoding='utf-8')
        for value in ('--config-dir /srv/lefteria-os-preview/acme/config', '--work-dir /srv/lefteria-os-preview/acme/work',
                      '--logs-dir /srv/lefteria-os-preview/acme/logs', '--cert-name srv1649602.hstgr.cloud',
                      '--deploy-hook "/usr/bin/python3 /srv/lefteria-os-preview/source/deploy/preview/renew-preview-tls.py"',
                      'CPUQuota=20%', 'MemoryMax=512M', 'Nice=10', 'IOSchedulingClass=idle', 'TimeoutStartSec=10min'):
            self.assertIn(value, service)
        self.assertNotIn('/etc/letsencrypt', service)
        self.assertNotIn('nginx', service)
        self.assertEqual(2, timer.count('OnCalendar='))
        self.assertIn('RandomizedDelaySec=1h', timer)
        self.assertIn('Persistent=true', timer)
        self.assertIn('Unit=lefteria-os-preview-renew.service', timer)


if __name__ == '__main__':
    unittest.main()
