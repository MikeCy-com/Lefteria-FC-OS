import contextlib
from datetime import datetime, timezone
import hashlib
import importlib.util
import io
from pathlib import Path, PurePosixPath
import stat
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('preview_certificate_publisher', Path(__file__).with_name('publish-certificate.py'))
publisher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publisher)


class PreviewCertificateChecks(unittest.TestCase):
    def setUp(self):
        self.clock = datetime(2026, 11, 1, tzinfo=timezone.utc).timestamp()

    def crypto(self, wrong_key=False, san=None, ends='Jan 29 12:00:00 2027 GMT'):
        def run(arguments, content=None):
            if arguments[0] == 'verify':
                self.assertIn('-verify_hostname', arguments)
                self.assertIn(publisher.HOST, arguments)
                self.assertIn('-no-CAfile', arguments)
                self.assertIn('/etc/ssl/certs', arguments)
                return b'verified'
            if '-ext' in arguments:
                return ('X509v3 Subject Alternative Name:\n    ' + (san or 'DNS:' + publisher.HOST) + '\n').encode()
            if '-startdate' in arguments:
                return ('notBefore=Oct 31 12:00:00 2026 GMT\nnotAfter=' + ends + '\n').encode()
            if '-pubkey' in arguments:
                return b'public-key-pem'
            if arguments[0] == 'pkey':
                return b'different-public-key' if wrong_key and '-in' in arguments else b'public-key-der'
            if arguments[0] == 'x509' and '-outform' in arguments:
                return b'certificate-der'
            self.fail('Unexpected OpenSSL operation.')
        return run

    def test_dedicated_san_validity_trust_and_matching_key_are_required(self):
        with patch.object(publisher, 'openssl', side_effect=self.crypto()) as crypto:
            fingerprint, expires = publisher.validate_pair(Path('/staging'), self.clock)
            self.assertEqual(hashlib.sha256(b'certificate-der').hexdigest(), fingerprint)
            self.assertEqual('2027-01-29T12:00:00+00:00', expires)
            self.assertTrue(any(call.args[0][0] == 'verify' for call in crypto.call_args_list))
        for san in ('DNS:lefteriafc.cy', 'DNS:' + publisher.HOST + ', DNS:lefteriafc.cy', 'DNS:*.hstgr.cloud'):
            with self.subTest(san=san), patch.object(publisher, 'openssl', side_effect=self.crypto(san=san)):
                with self.assertRaisesRegex(publisher.PublicationRefused, 'dedicated_hostname_san_required'):
                    publisher.validate_pair(Path('/staging'), self.clock)
        with patch.object(publisher, 'openssl', side_effect=self.crypto(wrong_key=True)):
            with self.assertRaisesRegex(publisher.PublicationRefused, 'certificate_key_mismatch'):
                publisher.validate_pair(Path('/staging'), self.clock)
        for now, expiry in ((self.clock, 'Nov  2 12:00:00 2026 GMT'), (self.clock - 7 * 86400, 'Jan 29 12:00:00 2027 GMT')):
            with self.subTest(now=now, expiry=expiry), patch.object(publisher, 'openssl', side_effect=self.crypto(ends=expiry)):
                with self.assertRaisesRegex(publisher.PublicationRefused, 'certificate_outside_validity_window'):
                    publisher.validate_pair(Path('/staging'), now)

    def test_archive_escape_is_refused_without_reading_the_target(self):
        info = SimpleNamespace(st_mode=stat.S_IFLNK | 0o777, st_uid=0)
        with patch.object(publisher, 'require_chain'), patch.object(Path, 'lstat', return_value=info), \
                patch.object(Path, 'readlink', return_value=Path('../../archive/elsewhere/fullchain1.pem')), \
                patch.object(Path, 'resolve', return_value=Path('/etc/letsencrypt/archive/lefteriafc.cy/fullchain1.pem')), \
                patch.object(publisher, 'read_regular') as read:
            with self.assertRaisesRegex(publisher.PublicationRefused, 'certificate_outside_dedicated_archive'):
                publisher.source_pair()
            read.assert_not_called()

    def test_same_certbot_generation_is_required(self):
        info = SimpleNamespace(st_mode=stat.S_IFLNK | 0o777, st_uid=0)
        archive = publisher.CONFIG / 'archive' / publisher.HOST
        with patch.object(publisher, 'require_chain'), patch.object(Path, 'lstat', return_value=info), \
                patch.object(Path, 'readlink', return_value=Path('../../archive/host/file.pem')), \
                patch.object(Path, 'resolve', side_effect=[archive / 'fullchain1.pem', archive / 'privkey2.pem']), \
                patch.object(publisher, 'read_regular', return_value=b'private-test-bytes'):
            with self.assertRaisesRegex(publisher.PublicationRefused, 'mixed_certbot_generations'):
                publisher.source_pair()

    def test_current_must_be_a_root_owned_relative_link_inside_versions(self):
        info = SimpleNamespace(st_mode=stat.S_IFLNK | 0o777, st_uid=0, st_dev=1, st_ino=2)
        for value in ('/etc/letsencrypt/live/host', '../outside', 'versions/not-a-fingerprint', 'versions/a/../../other'):
            with self.subTest(value=value), patch.object(Path, 'lstat', return_value=info), \
                    patch.object(Path, 'readlink', return_value=PurePosixPath(value)):
                with self.assertRaisesRegex(publisher.PublicationRefused, 'current_symlink_outside_versions'):
                    publisher.current_target(publisher.TLS / 'current')
        regular = SimpleNamespace(st_mode=stat.S_IFREG | 0o600, st_uid=0)
        with patch.object(Path, 'lstat', return_value=regular):
            with self.assertRaisesRegex(publisher.PublicationRefused, 'owned_current_symlink_required'):
                publisher.current_target(publisher.TLS / 'current')
        fingerprint = 'a' * 64
        with patch.object(Path, 'lstat', return_value=info), \
                patch.object(Path, 'readlink', return_value=PurePosixPath('versions/' + fingerprint)), \
                patch.object(Path, 'resolve', return_value=publisher.TLS / 'versions' / fingerprint), \
                patch.object(publisher, 'require_directory'):
            self.assertEqual(('versions/' + fingerprint, 1, 2), publisher.current_target(publisher.TLS / 'current'))

    def test_source_key_and_external_error_values_never_reach_logs(self):
        output = io.StringIO()
        with patch.object(sys, 'argv', ['publish-certificate.py']), \
                patch.object(publisher, 'publish', side_effect=RuntimeError('PRIVATE_KEY_TEST_VALUE')), contextlib.redirect_stderr(output):
            self.assertEqual(1, publisher.main())
        self.assertEqual('Preview certificate publication refused: guarded_operation_failed\n', output.getvalue())
        self.assertNotIn('PRIVATE_KEY_TEST_VALUE', output.getvalue())


if __name__ == '__main__':
    unittest.main()
