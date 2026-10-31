#!/usr/bin/env python3
"""Prepare a new, private Linux staging configuration; never deploy or rotate it.

The caller must first verify the GitHub release checksums and source commit and
load its images. This tool only inspects those local images, hashes one new gate
password with the already-local official Caddy image, and atomically publishes
two new root-only files. It never reads production configuration or contacts a
registry, database, provider, certificate service or remote Docker daemon.
"""

from __future__ import annotations

import argparse
import base64
import ctypes
import errno
import json
import os
from pathlib import Path
import re
import secrets
import stat
import subprocess
import sys


ROOT = Path("/srv/lefteria-os-preview")
REPOSITORY = "MikeCy-com/Lefteria-FC-OS"
HOSTNAME = "srv1649602.hstgr.cloud"
SOURCE_SHA = re.compile(r"[0-9a-f]{40}\Z")
IMAGE_ID = re.compile(r"sha256:[0-9a-f]{64}\Z")
STAMP = re.compile(r"[0-9]{8}[tT][0-9]{6}[zZ]\Z")
BCRYPT = re.compile(r"\$2[aby]\$14\$[./A-Za-z0-9]{53}\Z")
OFFICIAL_CADDY_DIGEST = re.compile(r"(?:docker\.io/library/)?caddy@sha256:[0-9a-f]{64}\Z")
SOURCE_FILES = ("roster-manifest.json", "staff-relationships-manifest.json",
                "accounts-manifest.json", "calendar-manifest.json")
DOCKER = ["/usr/bin/docker", "--host", "unix:///var/run/docker.sock"]
DOCKER_ENV = {"PATH": "/usr/sbin:/usr/bin:/sbin:/bin", "HOME": "/root"}


class PreparationRefused(Exception):
    """Only fixed reason codes may be printed by this program."""


class PrivateArgumentParser(argparse.ArgumentParser):
    def error(self, message):
        # argparse's normal error includes unrecognized argument values.
        refuse("invalid_arguments")


def refuse(code: str) -> None:
    raise PreparationRefused(code)


def unique_object(pairs):
    value = {}
    for key, child in pairs:
        if key in value:
            refuse("duplicate_json_key")
        value[key] = child
    return value


def directory_info_safe(info, private: bool = False) -> bool:
    return (stat.S_ISDIR(info.st_mode) and info.st_uid == 0
            and not info.st_mode & 0o022
            and (not private or stat.S_IMODE(info.st_mode) == 0o700))


def file_info_safe(info, maximum: int) -> bool:
    return (stat.S_ISREG(info.st_mode) and info.st_uid == 0
            and stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1
            and 0 < info.st_size <= maximum)


def private_directory(path: Path, private: bool = False) -> None:
    if not directory_info_safe(path.lstat(), private):
        refuse("unsafe_directory")


def protected_chain(path: Path) -> None:
    # lstat rejects symlink components, including aliases to the preview root.
    for component in [*reversed(path.parents), path]:
        private_directory(component, private=component == ROOT or ROOT in component.parents)


def protected_file(path: Path, maximum: int) -> None:
    protected_chain(path.parent)
    if not file_info_safe(path.lstat(), maximum):
        refuse("unsafe_private_file")


def lexical_path(raw: str) -> Path:
    path = Path(raw)
    if not path.is_absolute() or ".." in path.parts or str(path) != raw:
        refuse("explicit_absolute_path_required")
    try:
        path.relative_to(ROOT)
    except ValueError:
        refuse("path_outside_preview")
    return path


def validate_paths(bundle: Path, output: Path, manifests: Path, certificates: Path) -> None:
    if output != ROOT / "config" or certificates != ROOT / "tls":
        refuse("unexpected_preview_directory")
    if manifests.parent != ROOT / "manifests" or not STAMP.fullmatch(manifests.name):
        refuse("explicit_manifest_snapshot_required")
    protected_chain(output.parent)
    try:
        output.lstat()
    except FileNotFoundError:
        pass
    else:
        refuse("output_already_exists")
    protected_chain(manifests)
    protected_chain(certificates)
    protected_file(bundle, 32768)
    for name in SOURCE_FILES:
        protected_file(manifests / name, 32 * 1024 * 1024)


def read_manifest(path: Path, source_sha: str) -> dict:
    # O_NOFOLLOW also guards the final open after the metadata check.
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(descriptor, "rb") as source:
        if not file_info_safe(os.fstat(source.fileno()), 32768):
            refuse("unsafe_bundle_manifest")
        raw = source.read(32769)
    if not 0 < len(raw) <= 32768:
        refuse("bundle_manifest_size")
    value = json.loads(raw.decode("utf-8", errors="strict"), object_pairs_hook=unique_object)
    return validate_manifest(value, source_sha)


def validate_manifest(value: dict, source_sha: str) -> dict:
    if (not isinstance(value, dict) or set(value) != {"schema_version", "repository", "source_sha", "platform", "images"}
            or type(value.get("schema_version")) is not int or value["schema_version"] != 1
            or value.get("repository") != REPOSITORY or value.get("source_sha") != source_sha
            or not SOURCE_SHA.fullmatch(source_sha) or value.get("platform") != "linux/amd64"
            or not isinstance(value.get("images"), dict) or set(value["images"]) != {"api", "web", "import"}):
        refuse("invalid_bundle_manifest")
    for name, image in value["images"].items():
        if (not isinstance(image, dict) or set(image) != {"reference", "id", "archive"}
                or image.get("reference") != f"lefteria-fc-os-{name}:{source_sha}"
                or not isinstance(image.get("id"), str) or not IMAGE_ID.fullmatch(image["id"])
                or image.get("archive") != f"lefteria-fc-os-{name}.tar.gz"):
            refuse("invalid_bundle_image")
    if len({image["id"] for image in value["images"].values()}) != 3:
        refuse("distinct_images_required")
    return value


def docker(arguments: list[str], data: bytes | None = None, timeout: int = 30) -> bytes:
    # Explicit local socket and a clean child environment prevent an ambient
    # DOCKER_HOST/context or application secret from reaching this operation.
    result = subprocess.run([*DOCKER, *arguments], input=data, stdout=subprocess.PIPE,
                            stderr=subprocess.PIPE, timeout=timeout, check=False, env=DOCKER_ENV)
    if result.returncode != 0 or len(result.stdout) > 1024 * 1024:
        refuse("local_docker_operation_failed")
    return result.stdout


def inspect_image(reference: str) -> dict:
    template = ('{"id":{{json .Id}},"os":{{json .Os}},"architecture":{{json .Architecture}},'
                '"labels":{{json .Config.Labels}},"digests":{{json .RepoDigests}}}')
    raw = docker(["image", "inspect", "--format", template, reference])
    value = json.loads(raw.decode("utf-8", errors="strict"), object_pairs_hook=unique_object)
    if not isinstance(value, dict):
        refuse("invalid_local_image_metadata")
    return value


def validate_loaded_images(manifest: dict) -> dict[str, str]:
    result = {}
    for name, expected in manifest["images"].items():
        actual = inspect_image(expected["reference"])
        labels = actual.get("labels")
        if (actual.get("id") != expected["id"] or actual.get("os") != "linux" or actual.get("architecture") != "amd64"
                or not isinstance(labels, dict) or labels.get("org.opencontainers.image.revision") != manifest["source_sha"]
                or labels.get("org.opencontainers.image.source") != f"https://github.com/{REPOSITORY}"):
            refuse("loaded_image_provenance_mismatch")
        # Use immutable local image IDs in Compose, rather than mutable tags.
        result[name] = expected["id"]
    return result


def fresh_secrets() -> dict[str, str]:
    entropy = [secrets.token_bytes(32) for _ in range(5)]
    if len(set(entropy)) != 5:
        refuse("independent_entropy_required")
    owner, application, signing, otp, gate = entropy
    url_value = lambda value: base64.urlsafe_b64encode(value).decode("ascii").rstrip("=")
    return {"owner": url_value(owner), "application": url_value(application),
            "signing": base64.b64encode(signing).decode("ascii"),
            "otp": base64.b64encode(otp).decode("ascii"), "gate": url_value(gate)}


def gate_hash(password: str) -> str:
    actual = inspect_image("caddy:2-alpine")
    image_id = actual.get("id")
    digests = actual.get("digests")
    if (not isinstance(image_id, str) or not IMAGE_ID.fullmatch(image_id)
            or actual.get("os") != "linux" or actual.get("architecture") != "amd64"
            or not isinstance(digests, list)
            or not any(isinstance(value, str) and OFFICIAL_CADDY_DIGEST.fullmatch(value) for value in digests)):
        refuse("official_local_caddy_image_required")
    # Omitting --plaintext is the documented stdin mode; there is no --stdin
    # flag. Password bytes never appear in argv, env, labels or log output.
    # https://caddyserver.com/docs/command-line#caddy-hash-password
    raw = docker(["run", "--rm", "--pull=never", "--network", "none", "--read-only", "--cap-drop", "ALL",
                  "--cap-add", "NET_BIND_SERVICE",
                  "--security-opt", "no-new-privileges", "--user", "65534:65534", "--memory", "64m",
                  "--cpus", "0.10", "--pids-limit", "32", "--entrypoint", "caddy", "-i", image_id,
                  "hash-password", "--algorithm", "bcrypt", "--bcrypt-cost", "14"],
                 password.encode("ascii") + b"\n", timeout=120)
    value = raw.decode("ascii", errors="strict").strip()
    if not BCRYPT.fullmatch(value):
        refuse("invalid_gate_hash")
    return value


def dotenv_value(value: str) -> str:
    if (not isinstance(value, str) or not value or "'" in value or "\\" in value
            or any(ord(character) < 32 or ord(character) > 126 for character in value)):
        refuse("unsafe_dotenv_value")
    # Single quotes preserve every literal bcrypt '$' through Compose parsing.
    return "'" + value + "'"


def configuration_files(source_sha: str, images: dict[str, str], manifests: Path,
                        certificates: Path, credentials: dict[str, str], bcrypt_hash: str) -> dict[str, bytes]:
    environment = {
        "PREVIEW_SOURCE_SHA": source_sha, "PREVIEW_HOST": HOSTNAME, "PREVIEW_BIND": "127.0.0.1",
        "PREVIEW_CERT_DIRECTORY": str(certificates), "PREVIEW_MANIFEST_DIRECTORY": str(manifests),
        "PREVIEW_API_IMAGE": images["api"], "PREVIEW_WEB_IMAGE": images["web"], "PREVIEW_IMPORT_IMAGE": images["import"],
        "PREVIEW_OWNER_PASSWORD": credentials["owner"], "PREVIEW_APP_PASSWORD": credentials["application"],
        "PREVIEW_SIGNING_KEY": credentials["signing"], "PREVIEW_OTP_KEY": credentials["otp"], "PREVIEW_GATE_HASH": bcrypt_hash,
    }
    content = "# Dedicated preview only. Keep private; do not source as a shell script.\n"
    content += "".join(f"{name}={dotenv_value(value)}\n" for name, value in environment.items())
    access = {"url": f"https://{HOSTNAME}:8443", "username": "preview", "password": credentials["gate"]}
    return {".env": content.encode("ascii"),
            "client-access.json": (json.dumps(access, indent=2, sort_keys=True) + "\n").encode("ascii")}


def identity(info) -> tuple[int, int]:
    return info.st_dev, info.st_ino


def publish_new_directory(staging: Path, output: Path) -> None:
    # Linux renameat2(RENAME_NOREPLACE) atomically publishes the whole directory
    # and cannot replace an existing empty directory, file or symlink.
    library = ctypes.CDLL(None, use_errno=True)
    rename = getattr(library, "renameat2", None)
    if rename is None:
        refuse("atomic_no_replace_unavailable")
    rename.argtypes = [ctypes.c_int, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p, ctypes.c_uint]
    rename.restype = ctypes.c_int
    if rename(-100, os.fsencode(staging), -100, os.fsencode(output), 1) != 0:
        if ctypes.get_errno() in (errno.EEXIST, errno.ENOTEMPTY):
            refuse("output_already_exists")
        refuse("atomic_publication_failed")


def cleanup_created_directory(staging: Path, directory_identity: tuple[int, int], files: dict[str, tuple[int, int]]) -> bool:
    try:
        info = staging.lstat()
        if not directory_info_safe(info, private=True) or identity(info) != directory_identity:
            return False
        if {path.name for path in staging.iterdir()} != set(files):
            return False
        # Verify every entry before deleting any. Never remove unknown files,
        # replacement inodes, nested directories or a directory supplied by others.
        for name, expected in files.items():
            actual = (staging / name).lstat()
            if (not stat.S_ISREG(actual.st_mode) or actual.st_uid != 0 or actual.st_nlink != 1
                    or identity(actual) != expected):
                return False
        for name in files:
            (staging / name).unlink()
        staging.rmdir()
        return True
    except OSError:
        return False


def write_configuration(output: Path, files: dict[str, bytes]) -> None:
    if set(files) != {".env", "client-access.json"}:
        refuse("unexpected_configuration_files")
    protected_chain(output.parent)
    staging = output.parent / (".config.prepare-" + secrets.token_hex(16))
    staging.mkdir(mode=0o700)
    os.chmod(staging, 0o700, follow_symlinks=False)
    directory_identity = identity(staging.lstat())
    created = {}
    published = False
    try:
        private_directory(staging, private=True)
        for name, content in files.items():
            descriptor = os.open(staging / name, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
            created[name] = identity(os.fstat(descriptor))
            with os.fdopen(descriptor, "wb") as target:
                os.fchmod(target.fileno(), 0o600)
                target.write(content)
                target.flush()
                os.fsync(target.fileno())
        directory = os.open(staging, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
        protected_chain(output.parent)
        publish_new_directory(staging, output)
        published = True
        directory = os.open(output.parent, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
        try:
            os.fsync(directory)
        finally:
            os.close(directory)
    finally:
        if not published and not cleanup_created_directory(staging, directory_identity, created):
            refuse("private_preparation_state_retained")


def prepare(args) -> None:
    if sys.platform != "linux" or not hasattr(os, "geteuid") or os.geteuid() != 0:
        refuse("linux_root_required")
    if not SOURCE_SHA.fullmatch(args.source_sha):
        refuse("full_source_sha_required")
    bundle, output, manifests, certificates = [lexical_path(value) for value in
        (args.bundle_manifest, args.output_directory, args.manifests_directory, args.certificate_directory)]
    validate_paths(bundle, output, manifests, certificates)
    manifest = read_manifest(bundle, args.source_sha)
    images = validate_loaded_images(manifest)
    credentials = fresh_secrets()
    bcrypt_hash = gate_hash(credentials["gate"])
    files = configuration_files(args.source_sha, images, manifests, certificates, credentials, bcrypt_hash)
    write_configuration(output, files)


def main(arguments=None) -> int:
    parser = PrivateArgumentParser(description="Prepare a new private preview configuration; does not deploy.")
    for name in ("source-sha", "bundle-manifest", "output-directory", "manifests-directory", "certificate-directory"):
        parser.add_argument("--" + name, required=True)
    try:
        args = parser.parse_args(arguments)
        prepare(args)
    except PreparationRefused as error:
        print("Preview environment preparation refused: " + str(error), file=sys.stderr)
        return 1
    except Exception:
        # Image output, paths, subprocess errors and secret material are never
        # included in stdout/stderr, even when a lower-level operation fails.
        print("Preview environment preparation refused: guarded_operation_failed", file=sys.stderr)
        return 1
    print("Prepared private preview configuration; the edge remains bound to loopback.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
