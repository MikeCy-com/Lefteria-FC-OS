"""Publish renewed preview TLS and reload only its verified Compose edge."""

from pathlib import Path
import os
import re
import stat
import subprocess
import sys

ROOT = Path('/srv/lefteria-os-preview')
PUBLISHER = ROOT / 'source/deploy/preview/publish-certificate.py'
PROJECT = 'lefteria-os-preview'
SERVICE = 'edge'
PYTHON = '/usr/bin/python3'
DOCKER = '/usr/bin/docker'
DOCKER_HOST = 'unix:///var/run/docker.sock'
SAFE_ENV = {'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'HOME': '/nonexistent',
            'LANG': 'C.UTF-8', 'PYTHONDONTWRITEBYTECODE': '1'}


class RenewalRefused(Exception):
    pass


def verify_publisher():
    if getattr(os, 'geteuid', lambda: -1)() != 0:
        raise RenewalRefused('root_required')
    for directory in reversed(PUBLISHER.parent.parents):
        info = directory.lstat()
        if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
            raise RenewalRefused('unsafe_publisher_path')
    info = PUBLISHER.parent.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
        raise RenewalRefused('unsafe_publisher_path')
    info = PUBLISHER.lstat()
    if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022
            or info.st_nlink != 1 or not 1 <= info.st_size <= 128 * 1024):
        raise RenewalRefused('unsafe_publisher_file')


def run_checked(arguments, timeout=30):
    result = subprocess.run(arguments, capture_output=True, check=False,
                            timeout=timeout, env=dict(SAFE_ENV))
    if result.returncode or len(result.stdout) > 16384 or len(result.stderr) > 16384:
        raise RenewalRefused('guarded_command_failed')
    return result.stdout


def docker(*arguments):
    # No ambient Docker context/remote daemon or provider credentials are used.
    return run_checked([DOCKER, '--host', DOCKER_HOST, *arguments])


def verified_edge():
    result = docker('ps', '--no-trunc', '--filter', 'label=com.docker.compose.project=' + PROJECT,
                    '--filter', 'label=com.docker.compose.service=' + SERVICE,
                    '--filter', 'status=running', '--format', '{{.ID}}').decode('ascii')
    identifiers = result.splitlines()
    if not identifiers:
        raise RenewalRefused('preview_edge_not_running')
    if len(identifiers) != 1 or not re.fullmatch('[0-9a-f]{64}', identifiers[0]):
        raise RenewalRefused('preview_edge_ambiguous')
    identifier = identifiers[0]
    template = ('{{.Id}}|{{.State.Running}}|{{.State.Paused}}|{{.State.Restarting}}|'
                '{{index .Config.Labels "com.docker.compose.project"}}|'
                '{{index .Config.Labels "com.docker.compose.service"}}')
    actual = docker('inspect', '--type', 'container', '--format', template, identifier).decode('ascii').strip()
    if actual != '|'.join((identifier, 'true', 'false', 'false', PROJECT, SERVICE)):
        raise RenewalRefused('preview_edge_identity_changed')
    return identifier


def renew():
    verify_publisher()
    run_checked([PYTHON, str(PUBLISHER)], timeout=120)
    identifier = verified_edge()
    # An immutable checked ID is used, never a name, wildcard or Compose-wide
    # reload. If that edge stops/replaces itself, this command fails closed.
    docker('exec', identifier, 'caddy', 'reload', '--config', '/etc/caddy/Caddyfile', '--adapter', 'caddyfile')


def main():
    try:
        if len(sys.argv) != 1:
            raise RenewalRefused('arguments_not_supported')
        renew()
        print('Preview certificate published and verified preview edge reloaded.')
        return 0
    except RenewalRefused as error:
        print('Preview TLS renewal refused: ' + str(error), file=sys.stderr)
    except Exception:
        print('Preview TLS renewal refused: guarded_operation_failed', file=sys.stderr)
    return 1


if __name__ == '__main__':
    raise SystemExit(main())
