#!/usr/bin/env python3
"""Exercise a full PostgreSQL custom-format backup + isolated restore in CI only.

This tool intentionally refuses any production/preview database or Docker
context. It produces no hosting credentials, exports no protected data, and
never deploys; it is a repeatable *synthetic* disaster-recovery acceptance test.
A real, separately encrypted and off-host production restore drill remains a
distinct release gate.
"""
from __future__ import annotations

import json
import os
import re
import secrets
import subprocess
import sys
import tempfile
from pathlib import Path

REPOSITORY = "MikeCy-com/Lefteria-FC-OS"
ORIGINAL_DATABASE = "lefteria_rewrite_identity_tests"
RESTORED_DATABASE = "lefteria_os_restored_disposable_ci"
CONTAINER = "identity-postgres"
EXPECTED_PURPOSE = "Lefteria rewrite identity integration tests only"
SAFE_ACCOUNT = re.compile(r"^[a-z_][a-z_0-9]{0,62}$")
DRILL_SENTINEL = "lefteria_os_disposable_restore_sentinel"


def validate_environment(env: dict[str, str], fixture: dict) -> str:
    """Fail closed if anything resembles a live, preview or unreviewed target."""
    if (env.get("GITHUB_ACTIONS") != "true"
        or env.get("GITHUB_REPOSITORY") != REPOSITORY
        or env.get("LEFTERIA_RESTORE_DRILL_DISPOSABLE_ONLY") != "1"
        or env.get("POSTGRES_DB") != ORIGINAL_DATABASE
        or not isinstance(fixture, dict)
        or fixture.get("disposable") is not True
        or fixture.get("purpose") != EXPECTED_PURPOSE
        or fixture.get("host") != "127.0.0.1"
        or fixture.get("port") != 54329
        or fixture.get("database") != ORIGINAL_DATABASE
        or fixture.get("username") != env.get("POSTGRES_USER")):
        raise ValueError("Only the exact disposable GitHub identity database is permitted")
    owner = fixture["username"]
    if not isinstance(owner, str) or not SAFE_ACCOUNT.fullmatch(owner):
        raise ValueError("Unexpected PostgreSQL owner identifier")
    return owner


def quote_identifier(value: str) -> str:
    return '"' + value.replace('"', '""') + '"'


def quote_literal(value: str) -> str:
    return "'" + value.replace("'", "''") + "'"


def fingerprint_query(relations: list[tuple[str, str]]) -> str:
    """Stable row-content fingerprints without displaying any record values."""
    if not relations or len(relations) > 1000:
        raise ValueError("Unexpected/empty database catalog")
    if len(relations) != len(set(relations)):
        raise ValueError("Duplicate database relation in fingerprint")
    parts = []
    for schema, table in relations:
        if not schema or not table or schema.startswith("pg_") or schema == "information_schema":
            raise ValueError("Unsafe catalog relation")
        relation = quote_literal(schema + "." + table)
        table_name = quote_identifier(schema) + "." + quote_identifier(table)
        parts.append(
            "SELECT " + relation + " AS relation, count(*)::bigint AS records, "
            "md5(coalesce(string_agg(md5(row_to_json(t)::text), '' "
            "ORDER BY md5(row_to_json(t)::text)),'')) AS content_digest "
            "FROM " + table_name + " AS t"
        )
    return "SELECT * FROM (" + " UNION ALL ".join(parts) + ") AS catalog ORDER BY relation;"


def docker(*args: str, input_file=None, output_file=None, capture=False) -> str:
    cmd = ["docker", *args]
    result = subprocess.run(
        cmd, stdin=input_file, stdout=subprocess.PIPE if capture else output_file,
        stderr=subprocess.PIPE, check=False, timeout=180,
    )
    if result.returncode:
        # Never surface container output: it can include SQL/connection data.
        raise RuntimeError("Disposable restore drill command failed: " + args[0])
    return result.stdout.decode("utf-8").strip() if capture else ""


def psql(owner: str, database: str, statement: str) -> str:
    return docker("exec", CONTAINER, "psql", "-X", "-v", "ON_ERROR_STOP=1",
                  "-U", owner, "-d", database, "-A", "-t", "-c", statement,
                  capture=True)


def catalog(owner: str, database: str) -> list[tuple[str, str]]:
    rows = psql(owner, database, """
        SELECT schemaname||E'\\t'||tablename FROM pg_catalog.pg_tables
        WHERE schemaname<>'information_schema'
          AND schemaname NOT LIKE 'pg_%'
        ORDER BY schemaname COLLATE "C",tablename COLLATE "C";
    """)
    return [tuple(item.split("\t", 1)) for item in rows.splitlines() if item]


def main() -> None:
    fixture_path = os.environ.get("LEFTERIA_TEST_FIXTURE")
    if not fixture_path:
        raise ValueError("Disposable test database fixture is required")
    fixture = json.loads(Path(fixture_path).read_text(encoding="utf-8"))
    owner = validate_environment(dict(os.environ), fixture)

    # Do not touch a similarly named container on a real Docker host.
    image = docker("inspect", "--format", "{{.Config.Image}}", CONTAINER,
                   capture=True)
    if not re.fullmatch(r"postgres:18(?:\.[0-9]+)?-alpine", image):
        raise ValueError("Unexpected Docker image; refusing unsafe restore")
    if psql(owner, "postgres", "SELECT EXISTS(SELECT 1 FROM pg_database "
            f"WHERE datname={quote_literal(RESTORED_DATABASE)});") != "f":
        raise ValueError("Restore database already exists; refusing to overwrite it")
    if psql(owner, ORIGINAL_DATABASE,
            f"SELECT to_regclass('public.{DRILL_SENTINEL}') IS NULL;") != "t":
        raise ValueError("Disposable sentinel already exists; refusing to overwrite it")

    sentinel_created = False
    restore_created = False
    with tempfile.TemporaryDirectory(prefix="lefteria-disposable-restore-") as path:
        snapshot = Path(path) / "pg-backup.custom"
        marker = secrets.token_hex(32)  # never a real guardian or financial row
        try:
            psql(owner, ORIGINAL_DATABASE,
                 f"CREATE TABLE public.{DRILL_SENTINEL} "
                 "(marker text PRIMARY KEY, supplied_at timestamptz NOT NULL DEFAULT now()); "
                 f"INSERT INTO public.{DRILL_SENTINEL}(marker) "
                 f"VALUES({quote_literal(marker)});")
            sentinel_created = True

            source_relations = catalog(owner, ORIGINAL_DATABASE)
            if ("public", DRILL_SENTINEL) not in source_relations:
                raise RuntimeError("Synthetic sentinel not in source catalog")
            source_fingerprint = psql(owner, ORIGINAL_DATABASE,
                                      fingerprint_query(source_relations))

            with snapshot.open("wb") as archive:
                docker("exec", CONTAINER, "pg_dump", "--format=custom",
                       "--no-owner", "-U", owner, "-d", ORIGINAL_DATABASE,
                       output_file=archive)
            if snapshot.stat().st_size < 4096:
                raise RuntimeError("Unreasonably small PostgreSQL backup")
            docker("exec", CONTAINER, "createdb", "-U", owner,
                   "--template=template0", RESTORED_DATABASE)
            restore_created = True
            with snapshot.open("rb") as archive:
                docker("exec", "-i", CONTAINER, "pg_restore", "--exit-on-error",
                       "--no-owner", "-U", owner, "-d", RESTORED_DATABASE,
                       input_file=archive)

            target_relations = catalog(owner, RESTORED_DATABASE)
            if target_relations != source_relations:
                raise RuntimeError("Restored schema/catalog is incomplete")
            restored_fingerprint = psql(owner, RESTORED_DATABASE,
                                         fingerprint_query(target_relations))
            if restored_fingerprint != source_fingerprint:
                raise RuntimeError("Restored database content does not match backup")
            if psql(owner, RESTORED_DATABASE,
                    f"SELECT count(*) FROM public.{DRILL_SENTINEL} "
                    f"WHERE marker={quote_literal(marker)};") != "1":
                raise RuntimeError("Restored sentinel row was lost")
            print(f"Disposable PostgreSQL backup/restore verified: "
                  f"{len(source_relations)} relations, including row content.")
        finally:
            # Both targets are positively identified disposable CI artifacts.
            # Never use --clean/--create against the authoritative source DB.
            if restore_created:
                docker("exec", CONTAINER, "dropdb", "--if-exists",
                       "-U", owner, RESTORED_DATABASE)
            if sentinel_created:
                psql(owner, ORIGINAL_DATABASE,
                     f"DROP TABLE public.{DRILL_SENTINEL};")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, OSError, subprocess.TimeoutExpired,
            json.JSONDecodeError) as exc:
        print("Disposable restore verification FAILED (no live operation): "
              + str(exc), file=sys.stderr)
        sys.exit(1)
