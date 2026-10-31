"""Capture the existing production ACME route without changing its configuration."""

import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess

ROOT = Path('/srv/lefteria-os-preview')
HOST = 'srv1649602.hstgr.cloud'


def docker(*args):
    result = subprocess.run(['docker', *args], capture_output=True, timeout=30)
    if result.returncode or len(result.stdout) > 1024 * 1024:
        raise ValueError('docker_probe_failed')
    return result.stdout


def private_directory(path):
    if not path.exists():
        path.mkdir(mode=0o700)
    info = path.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o077:
        raise ValueError('private_directory_required')


def write_once(path, value):
    content = (json.dumps(value, sort_keys=True, indent=2) + '\n').encode()
    if path.exists():
        info = path.lstat()
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o077 or path.read_bytes() != content:
            raise ValueError('existing_guard_differs')
        return
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'wb') as target:
        target.write(content)
        target.flush()
        os.fsync(target.fileno())


def main():
    if os.geteuid() != 0 or ROOT.resolve() != ROOT:
        raise ValueError('dedicated_root_required')
    private_directory(ROOT)
    directory = ROOT / 'acme'
    private_directory(directory)
    for name in ('config', 'work', 'logs'):
        private_directory(directory / name)
    names = ['lefteria-frontend', 'lefteria-backend', 'lefteria-mongo']
    records = json.loads(docker('inspect', *names))
    if len(records) != 3:
        raise ValueError('production_inventory_changed')
    baseline = []
    for record, name in zip(records, names):
        if (record['Name'] != '/' + name or not record['State']['Running'] or
                record['Config']['Labels'].get('com.docker.compose.project') != 'lefteriafc'):
            raise ValueError('production_container_changed')
        baseline.append({'name': name, 'id': record['Id'], 'image': record['Image'],
                         'started_at': record['State']['StartedAt']})
    configuration = docker('exec', records[0]['Id'], 'nginx', '-T')
    if not re.search(rb'location\s+\^~\s+/\.well-known/acme-challenge/\s*\{[^{}]*\broot\s+/var/www/certbot\s*;', configuration):
        raise ValueError('existing_acme_route_required')
    guard = {'schema_version': 1, 'hostname': HOST, 'frontend_container_id': records[0]['Id'],
             'nginx_config_sha256': hashlib.sha256(configuration).hexdigest()}
    write_once(directory / 'production-baseline.json', baseline)
    write_once(directory / 'guard.json', guard)
    print('Dedicated preview ACME guard ready; production containers/configuration unchanged.')


if __name__ == '__main__':
    try:
        main()
    except Exception:
        raise SystemExit('Preview ACME initialization refused; no production configuration was changed.')
