"""Guarded HTTP-01 token files for the already-running legacy nginx route.

This does not install certificates, change nginx configuration or reload/stop it.
Only the preview's new ACME state and exact owned public challenge file change.
"""

from __future__ import annotations

import hashlib
import http.client
import json
import os
from pathlib import Path
import re
import secrets
import stat
import subprocess
import sys


HOSTNAME = "srv1649602.hstgr.cloud"
GUARD_ROOT = Path("/srv/lefteria-os-preview/acme")
FRONTEND_NAME = "lefteria-frontend"
HEX_DIGEST = re.compile(r"[0-9a-f]{64}\Z")
TOKEN = re.compile(r"[A-Za-z0-9_-]{16,256}\Z")
THUMBPRINT = re.compile(r"[A-Za-z0-9_-]{16,256}\Z")


class HookRefused(Exception):
    """Only fixed non-sensitive reason codes reach the process output."""


def refused(code: str) -> None:
    raise HookRefused(code)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            refused("duplicate_guard_key")
        result[key] = value
    return result


def private_directory(path: Path) -> None:
    info = path.lstat()
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
        refused("unsafe_guard_directory")


def private_json(path: Path) -> dict:
    info = path.lstat()
    if (not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o077
            or info.st_nlink != 1 or not 1 <= info.st_size <= 16384):
        refused("unsafe_guard_file")
    with path.open("r", encoding="utf-8", errors="strict") as source:
        value = json.load(source, object_pairs_hook=unique_object)
    if not isinstance(value, dict):
        refused("invalid_guard_object")
    return value


def validate_guard(value: dict) -> dict:
    if (set(value) != {"schema_version", "hostname", "frontend_container_id", "nginx_config_sha256"}
            or type(value.get("schema_version")) is not int or value["schema_version"] != 1
            or value.get("hostname") != HOSTNAME
            or not isinstance(value.get("frontend_container_id"), str)
            or not HEX_DIGEST.fullmatch(value["frontend_container_id"])
            or not isinstance(value.get("nginx_config_sha256"), str)
            or not HEX_DIGEST.fullmatch(value["nginx_config_sha256"])):
        refused("invalid_preview_guard")
    return value


def load_guard() -> tuple[Path, dict]:
    raw = os.environ.get("PREVIEW_ACME_GUARD_FILE", "")
    path = Path(raw)
    if not path.is_absolute() or ".." in path.parts:
        refused("guard_path_required")
    try:
        path.relative_to(GUARD_ROOT)
    except ValueError:
        refused("guard_path_outside_preview")
    # No symlink component or writable-by-others directory may supply the guard.
    for directory in reversed(path.parent.parents):
        private_directory(directory)
    private_directory(path.parent)
    return path, validate_guard(private_json(path))


def validate_challenge(environment: dict[str, str]) -> tuple[str, bytes]:
    domain = environment.get("CERTBOT_DOMAIN", "")
    token = environment.get("CERTBOT_TOKEN", "")
    validation = environment.get("CERTBOT_VALIDATION", "")
    if domain != HOSTNAME:
        refused("unexpected_acme_hostname")
    if not TOKEN.fullmatch(token):
        refused("invalid_acme_token")
    prefix = token + "."
    if not validation.startswith(prefix) or not THUMBPRINT.fullmatch(validation[len(prefix):]):
        refused("invalid_acme_validation")
    return token, validation.encode("ascii")


def docker(arguments: list[str], data: bytes | None = None) -> bytes:
    result = subprocess.run(["docker", *arguments], input=data, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, timeout=30, check=False)
    if result.returncode != 0 or len(result.stdout) > 1024 * 1024:
        refused("guarded_docker_operation_failed")
    return result.stdout


def verify_frontend(guard: dict) -> None:
    actual = docker(["inspect", "--type", "container", "--format",
                     "{{.Id}}|{{.State.Running}}|{{.Name}}", FRONTEND_NAME]).decode("ascii").strip()
    if actual != guard["frontend_container_id"] + "|true|/" + FRONTEND_NAME:
        refused("production_frontend_changed")
    configuration = docker(["exec", guard["frontend_container_id"], "nginx", "-T"])
    if hashlib.sha256(configuration).hexdigest() != guard["nginx_config_sha256"]:
        refused("production_nginx_configuration_changed")
    if not re.search(rb"location\s+\^~\s+/\.well-known/acme-challenge/\s*\{[^{}]*\broot\s+/var/www/certbot\s*;", configuration):
        refused("existing_acme_route_missing")


# The only variable shell argument is the strictly validated token, passed as
# argv[1]. Validation bytes use stdin. There is no string-built shell command.
CREATE_TOKEN = r"""set -eu
umask 022
for directory in /var /var/www /var/www/certbot /var/www/certbot/.well-known /var/www/certbot/.well-known/acme-challenge; do
  test ! -L "$directory" || exit 71
  if test -e "$directory"; then
    test -d "$directory" || exit 72
  else
    mkdir "$directory"
    chmod 0755 "$directory"
  fi
  test "$(stat -c %u "$directory")" = 0 || exit 73
  case "$(stat -c %a "$directory")" in 755|750|700|555|550|500) ;; *) exit 74 ;; esac
done
file=/var/www/certbot/.well-known/acme-challenge/$1
# POSIX noclobber refuses an existing file or symlink. Nothing is overwritten.
set -C
cat > "$file"
chmod 0644 "$file"
"""

REMOVE_TOKEN = r"""set -eu
for directory in /var /var/www /var/www/certbot /var/www/certbot/.well-known /var/www/certbot/.well-known/acme-challenge; do
  test ! -L "$directory" || exit 71
  if ! test -e "$directory"; then exit 0; fi
  test -d "$directory" || exit 72
  test "$(stat -c %u "$directory")" = 0 || exit 73
  case "$(stat -c %a "$directory")" in 755|750|700|555|550|500) ;; *) exit 74 ;; esac
done
file=/var/www/certbot/.well-known/acme-challenge/$1
if ! test -e "$file" && ! test -L "$file"; then exit 0; fi
test -f "$file" && test ! -L "$file" || exit 75
test "$(stat -c %u:%a "$file")" = 0:644 || exit 76
# cmp includes trailing newlines, unlike shell command substitution.
cmp -s - "$file" || exit 77
rm -- "$file"
"""


def token_command(guard: dict, token: str, validation: bytes, script: str) -> None:
    docker(["exec", "--user", "0", "-i", guard["frontend_container_id"],
            "sh", "-c", script, "preview-acme", token], validation)


def ownership(guard: dict, token: str, validation: bytes, created: bool) -> dict:
    return {"schema_version": 1, "hostname": HOSTNAME, "token": token,
            "frontend_container_id": guard["frontend_container_id"],
            "nginx_config_sha256": guard["nginx_config_sha256"],
            "validation_sha256": hashlib.sha256(validation).hexdigest(), "created": created}


def state_path(guard_path: Path, token: str) -> Path:
    directory = guard_path.parent / "tokens"
    try:
        directory.mkdir(mode=0o700)
    except FileExistsError:
        pass
    private_directory(directory)
    if directory.stat().st_mode & 0o077:
        refused("unsafe_token_state_directory")
    return directory / (token + ".json")


def write_exclusive(path: Path, value: dict) -> None:
    descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, "w", encoding="ascii") as target:
        json.dump(value, target, ensure_ascii=True, sort_keys=True)
        target.write("\n")
        target.flush()
        os.fsync(target.fileno())


def replace_owned_state(path: Path, before: dict, after: dict) -> None:
    if private_json(path) != before:
        refused("token_ownership_changed")
    temporary = path.with_name(path.name + "." + secrets.token_hex(12) + ".tmp")
    write_exclusive(temporary, after)
    os.replace(temporary, path)


def discard_owned_state(path: Path, expected: dict) -> None:
    if private_json(path) != expected:
        refused("token_ownership_changed")
    path.unlink()


def verify_http(token: str, validation: bytes) -> None:
    connection = http.client.HTTPConnection("127.0.0.1", 80, timeout=10)
    try:
        connection.request("GET", "/.well-known/acme-challenge/" + token, headers={"Host": HOSTNAME})
        response = connection.getresponse()
        if response.status != 200 or response.read(1025) != validation:
            refused("existing_acme_route_did_not_serve_token")
    finally:
        connection.close()


def authenticate() -> None:
    guard_path, guard = load_guard()
    token, validation = validate_challenge(dict(os.environ))
    verify_frontend(guard)
    path = state_path(guard_path, token)
    pending = ownership(guard, token, validation, False)
    created = ownership(guard, token, validation, True)
    write_exclusive(path, pending)
    try:
        token_command(guard, token, validation, CREATE_TOKEN)
    except Exception:
        # No successful token creation was observed: never delete a production
        # file after an uncertain or refused create. Retain pending evidence.
        raise
    replace_owned_state(path, pending, created)
    try:
        verify_http(token, validation)
    except Exception:
        # We have durable ownership and exact expected bytes. Failure to remove
        # preserves the evidence for an operator rather than deleting blindly.
        verify_frontend(guard)
        token_command(guard, token, validation, REMOVE_TOKEN)
        discard_owned_state(path, created)
        raise


def cleanup() -> None:
    guard_path, guard = load_guard()
    token, validation = validate_challenge(dict(os.environ))
    verify_frontend(guard)
    path = state_path(guard_path, token)
    expected = ownership(guard, token, validation, True)
    if private_json(path) != expected:
        refused("created_token_ownership_required")
    token_command(guard, token, validation, REMOVE_TOKEN)
    discard_owned_state(path, expected)


def main(mode: str) -> int:
    try:
        if mode == "auth":
            authenticate()
        elif mode == "cleanup":
            cleanup()
        else:
            refused("unknown_hook_mode")
        return 0
    except HookRefused as error:
        print("Preview ACME hook refused: " + str(error), file=sys.stderr)
    except Exception:
        # Docker, JSON, path and HTTP errors must not print environment/input.
        print("Preview ACME hook refused: guarded_operation_failed", file=sys.stderr)
    return 1
