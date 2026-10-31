"""Publish only the dedicated preview certificate; never reload production."""

from __future__ import annotations

import ctypes
from datetime import datetime, timezone
import hashlib
import hmac
import json
import os
from pathlib import Path, PurePosixPath
import re
import secrets
import ssl
import stat
import subprocess
import sys

ROOT = Path('/srv/lefteria-os-preview')
HOST = 'srv1649602.hstgr.cloud'
CONFIG = ROOT / 'acme/config'
TLS = ROOT / 'tls'
NAMES = ('fullchain.pem', 'privkey.pem')
MINIMUM_REMAINING_SECONDS = 7 * 24 * 60 * 60


class PublicationRefused(Exception):
    pass


def require_directory(path, private=False):
    info = path.lstat()
    if (not stat.S_ISDIR(info.st_mode) or info.st_uid != 0
            or info.st_mode & (0o077 if private else 0o022)):
        raise PublicationRefused('unsafe_directory')


def require_chain(path):
    for item in reversed(path.parents):
        require_directory(item, private=item == ROOT)
    require_directory(path, private=path in (ROOT, CONFIG, TLS, TLS / 'versions'))


def read_regular(path, private=True):
    before = path.lstat()
    if (not stat.S_ISREG(before.st_mode) or before.st_uid != 0 or before.st_nlink != 1
            or before.st_mode & (0o077 if private else 0o022) or not 1 <= before.st_size <= 1024 * 1024):
        raise PublicationRefused('unsafe_certificate_file')
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(descriptor, 'rb') as source:
        actual = os.fstat(source.fileno())
        if (actual.st_dev, actual.st_ino, actual.st_size) != (before.st_dev, before.st_ino, before.st_size):
            raise PublicationRefused('certificate_file_changed')
        content = source.read(1024 * 1024 + 1)
    if len(content) != before.st_size:
        raise PublicationRefused('certificate_file_changed')
    return content


def source_pair():
    require_chain(CONFIG)
    live = CONFIG / 'live' / HOST
    archive = CONFIG / 'archive' / HOST
    require_chain(live)
    require_chain(archive)
    contents = {}
    generations = set()
    for name in NAMES:
        link = live / name
        info = link.lstat()
        if not stat.S_ISLNK(info.st_mode) or info.st_uid != 0 or link.readlink().is_absolute():
            raise PublicationRefused('dedicated_certbot_symlink_required')
        target = link.resolve(strict=True)
        prefix = name.removesuffix('.pem')
        match = re.fullmatch(re.escape(prefix) + r'([1-9][0-9]*)\.pem', target.name)
        if target.parent != archive or not match:
            raise PublicationRefused('certificate_outside_dedicated_archive')
        generations.add(match.group(1))
        contents[name] = read_regular(target, private=name == 'privkey.pem')
    if len(generations) != 1:
        raise PublicationRefused('mixed_certbot_generations')
    return contents


def openssl(arguments, content=None):
    result = subprocess.run(['openssl', *arguments], input=content, capture_output=True,
                            timeout=30, check=False)
    if result.returncode or len(result.stdout) > 1024 * 1024:
        raise PublicationRefused('certificate_validation_failed')
    return result.stdout


def validate_pair(directory, now=None):
    certificate = directory / 'fullchain.pem'
    key = directory / 'privkey.pem'
    san = openssl(['x509', '-in', str(certificate), '-noout', '-ext', 'subjectAltName']).decode('ascii')
    if san.partition('\n')[2].strip() != 'DNS:' + HOST:
        raise PublicationRefused('dedicated_hostname_san_required')
    dates = openssl(['x509', '-in', str(certificate), '-noout', '-startdate', '-enddate']).decode('ascii')
    values = dict(line.split('=', 1) for line in dates.strip().splitlines())
    starts = ssl.cert_time_to_seconds(values['notBefore'])
    ends = ssl.cert_time_to_seconds(values['notAfter'])
    clock = datetime.now(timezone.utc).timestamp() if now is None else now
    if starts > clock or ends - clock < MINIMUM_REMAINING_SECONDS:
        raise PublicationRefused('certificate_outside_validity_window')
    certificate_public = openssl(['x509', '-in', str(certificate), '-noout', '-pubkey'])
    certificate_public = openssl(['pkey', '-pubin', '-outform', 'DER'], certificate_public)
    key_public = openssl(['pkey', '-in', str(key), '-passin', 'pass:', '-pubout', '-outform', 'DER'])
    if not hmac.compare_digest(certificate_public, key_public):
        raise PublicationRefused('certificate_key_mismatch')
    # Verify the captured pair against the host's system trust directory. Do not
    # inherit an operator's alternative SSL_CERT_FILE as the certificate anchor.
    openssl(['verify', '-no-CAfile', '-no-CAstore', '-CApath', '/etc/ssl/certs',
             '-purpose', 'sslserver', '-verify_hostname', HOST,
             '-untrusted', str(certificate), str(certificate)])
    der = openssl(['x509', '-in', str(certificate), '-outform', 'DER'])
    return hashlib.sha256(der).hexdigest(), datetime.fromtimestamp(ends, timezone.utc).isoformat()


def write_private(path, content):
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'wb') as target:
        os.fchmod(target.fileno(), 0o600)
        target.write(content)
        target.flush()
        os.fsync(target.fileno())


def fsync_directory(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def publish_directory(source, target):
    # Linux RENAME_NOREPLACE also refuses an existing empty directory/symlink.
    library = ctypes.CDLL(None, use_errno=True)
    rename = library.renameat2
    rename.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    rename.restype = ctypes.c_int
    if rename(-100, os.fsencode(source), -100, os.fsencode(target), 1):
        raise PublicationRefused('certificate_version_not_new')


def current_target(link):
    try:
        info = link.lstat()
    except FileNotFoundError:
        return None
    if not stat.S_ISLNK(info.st_mode) or info.st_uid != 0:
        raise PublicationRefused('owned_current_symlink_required')
    relative = PurePosixPath(str(link.readlink()))
    if relative.is_absolute() or len(relative.parts) != 2 or relative.parts[0] != 'versions' or not re.fullmatch('[0-9a-f]{64}', relative.parts[1]):
        raise PublicationRefused('current_symlink_outside_versions')
    resolved = link.resolve(strict=True)
    expected = TLS / 'versions' / relative.parts[1]
    if resolved != expected:
        raise PublicationRefused('current_symlink_outside_versions')
    require_directory(resolved, private=True)
    return str(relative), info.st_dev, info.st_ino


def switch_current(fingerprint):
    if not re.fullmatch('[0-9a-f]{64}', fingerprint):
        raise PublicationRefused('invalid_certificate_fingerprint')
    require_directory(TLS / 'versions' / fingerprint, private=True)
    current = TLS / 'current'
    previous = current_target(current)
    temporary = TLS / ('.current-' + secrets.token_hex(12))
    os.symlink('versions/' + fingerprint, temporary)
    if current_target(current) != previous:
        temporary.unlink()
        raise PublicationRefused('current_certificate_changed')
    os.replace(temporary, current)
    fsync_directory(TLS)


def clean_pending(directory, identity, files):
    if not directory.exists():
        return
    info = directory.lstat()
    if (directory.parent != TLS / 'versions' or not re.fullmatch(r'\.incoming-[0-9a-f]{24}', directory.name)
            or not stat.S_ISDIR(info.st_mode) or (info.st_dev, info.st_ino) != identity
            or set(directory.iterdir()) != set(files)):
        raise PublicationRefused('pending_directory_changed')
    for path, expected in files.items():
        info = path.lstat()
        if not stat.S_ISREG(info.st_mode) or (info.st_dev, info.st_ino) != expected:
            raise PublicationRefused('pending_file_changed')
    for path in files:
        path.unlink()
    directory.rmdir()


def publish():
    if getattr(os, 'geteuid', lambda: -1)() != 0:
        raise PublicationRefused('root_required')
    require_chain(ROOT)
    for directory in (TLS, TLS / 'versions'):
        try:
            directory.mkdir(mode=0o700)
        except FileExistsError:
            pass
        require_directory(directory, private=True)
    # An advisory lock serializes this publisher/renewal job; no Docker socket,
    # production certificate directory or service configuration is touched.
    import fcntl
    descriptor = os.open(TLS / '.publish.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'wb') as lock:
        info = os.fstat(lock.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o077 or info.st_nlink != 1:
            raise PublicationRefused('unsafe_publication_lock')
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        # Read the live generation only after taking the publication lock; an
        # older waiting publisher cannot overwrite a newer published snapshot.
        contents = source_pair()
        pending = TLS / 'versions' / ('.incoming-' + secrets.token_hex(12))
        pending.mkdir(mode=0o700)
        info = pending.lstat()
        identity = info.st_dev, info.st_ino
        files = {}
        try:
            for name, content in contents.items():
                path = pending / name
                write_private(path, content)
                info = path.lstat()
                files[path] = info.st_dev, info.st_ino
            fingerprint, expires = validate_pair(pending)
            version = TLS / 'versions' / fingerprint
            reused = version.exists() or version.is_symlink()
            if reused:
                require_directory(version, private=True)
                if set(path.name for path in version.iterdir()) != set(NAMES) or any(read_regular(version / name) != contents[name] for name in NAMES):
                    raise PublicationRefused('existing_certificate_version_differs')
            else:
                fsync_directory(pending)
                publish_directory(pending, version)
                fsync_directory(TLS / 'versions')
            switch_current(fingerprint)
            return {'hostname': HOST, 'certificate_sha256': fingerprint, 'not_after': expires, 'reused': reused}
        finally:
            clean_pending(pending, identity, files)


def main():
    try:
        if len(sys.argv) != 1:
            raise PublicationRefused('arguments_not_supported')
        print(json.dumps(publish(), sort_keys=True))
        return 0
    except PublicationRefused as error:
        print('Preview certificate publication refused: ' + str(error), file=sys.stderr)
    except Exception:
        print('Preview certificate publication refused: guarded_operation_failed', file=sys.stderr)
    return 1


if __name__ == '__main__':
    raise SystemExit(main())
