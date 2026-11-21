#!/bin/sh
# CI ONLY. Exercises a fresh PostgreSQL custom-format backup and independent
# restore of an ephemeral test database. Not an operator backup command.
# Never point this script at a real user database or production host.
set -eu
umask 077

source_db='lefteria_rewrite_identity_tests'
restore_db='lefteria_ci_restore_only'
owner='lefteria_tests'
port='54329'

die() { printf '%s\n' "Refusing disposable restore drill: $*" >&2; exit 1; }

# Explicit double opt-in: a GitHub Actions-only environment flag and exact
# non-production cluster settings provisioned by the identity CI job.
[ "${LEFTERIA_DISPOSABLE_RESTORE_ACK:-}" = 'ci-only' ] ||
  die 'missing CI-only opt-in'
[ "${POSTGRES_DB:-}" = "$source_db" ] ||
  die 'unexpected source database'
[ "${POSTGRES_USER:-}" = "$owner" ] ||
  die 'unexpected test database owner'
[ "${POSTGRES_PASSWORD:-}" != '' ] ||
  die 'missing dedicated test database credentials'
[ "${PGHOST:-127.0.0.1}" = '127.0.0.1' ] ||
  die 'only local loopback PostgreSQL is allowed'
[ "${PGPORT:-54329}" = "$port" ] ||
  die 'only the fixed CI PostgreSQL port is allowed'

psql_source() {
  psql -X -A -t -q -v ON_ERROR_STOP=1 -h 127.0.0.1 -p "$port" -U "$owner" -d "$source_db" "$@"
}
psql_restore() {
  psql -X -A -t -q -v ON_ERROR_STOP=1 -h 127.0.0.1 -p "$port" -U "$owner" -d "$restore_db" "$@"
}
[ "$(psql_source -c 'SHOW listen_addresses')" = '127.0.0.1' ] ||
  die 'database is not explicitly loopback-bound'
[ "$(psql_source -c 'SHOW port')" = "$port" ] ||
  die 'database is not the dedicated CI instance'
[ "$(psql_source -c 'SELECT current_database()')" = "$source_db" ] ||
  die 'database identity mismatch'

# Do not overwrite or drop an unexpected existing database, even in CI.
[ "$(psql_source -c "SELECT count(*) FROM pg_database WHERE datname='$restore_db'")" = '0' ] ||
  die 'disposable restore database already exists'
[ "$(psql_source -c "SELECT count(*) FROM pg_class WHERE oid=to_regclass('public.lefteria_ci_restore_canary')")" = '0' ] ||
  die 'source contains a previous canary'

work_dir=$(mktemp -d /tmp/lefteria-disposable-restore.XXXXXX)
archive="$work_dir/isolated-ci.backup"
marker_created=0
restore_created=0

cleanup() {
  # Cleanup is scoped to the two exact, prevalidated disposable databases.
  # Never use a guessed, user-supplied or production database identifier.
  if [ "$marker_created" = 1 ]; then
    psql_source -c 'DROP TABLE IF EXISTS public.lefteria_ci_restore_canary' >/dev/null || :
  fi
  if [ "$restore_created" = 1 ]; then
    dropdb --if-exists -h 127.0.0.1 -p "$port" -U "$owner" "$restore_db" >/dev/null || :
  fi
  rm -rf "$work_dir"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

# This synthetic payload proves actual row data and its CHECK constraint
# survive the archive. No player/guardian, merchant, document or auth data is
# generated, exported or reported by the test.
psql_source -c "
  CREATE TABLE public.lefteria_ci_restore_canary (
    id integer PRIMARY KEY,
    value text NOT NULL CHECK (value='synthetic-ci-only')
  );
  INSERT INTO public.lefteria_ci_restore_canary(id,value)
  VALUES (1,'synthetic-ci-only');
" >/dev/null
marker_created=1

# Schema+data manifest is only small aggregate counts; never emit rows,
# passwords or actual private table contents into GitHub Actions logs.
manifest_sql="
SELECT concat_ws(':',
  (SELECT count(*) FROM public.\"__EFMigrationsHistory\"),
  (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
    WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname NOT IN
      ('information_schema') AND c.relkind IN ('r','p','v','m','S','f')),
  (SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
    WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname<>'information_schema'),
  (SELECT count(*) FROM pg_policy),
  (SELECT count(*) FROM pg_constraint c JOIN pg_namespace n
    ON n.oid=c.connamespace WHERE n.nspname NOT LIKE 'pg_%'
      AND n.nspname<>'information_schema'),
  (SELECT count(*) FROM club.staff),
  (SELECT count(*) FROM public.lefteria_ci_restore_canary)
)"
source_manifest=$(psql_source -c "$manifest_sql")
[ "$(printf '%s' "$source_manifest" | cut -d: -f1)" -gt 0 ] ||
  die 'migration history missing'
[ "$(printf '%s' "$source_manifest" | cut -d: -f4)" -gt 0 ] ||
  die 'security policies missing'

# Archive never leaves the container. Not a real backup: production needs
# independently encrypted off-host copies, retention and an operator restore.
pg_dump -h 127.0.0.1 -p "$port" -U "$owner" --format=custom   --compress=5 --no-owner --no-acl --file="$archive" "$source_db"
[ -s "$archive" ] || die 'backup archive is empty'
pg_restore --list "$archive" >/dev/null || die 'backup format is invalid'

# Drop the source-only synthetic row after the dump so success must come
# from an actual restore, not from accidentally querying the source.
psql_source -c 'DROP TABLE public.lefteria_ci_restore_canary' >/dev/null
marker_created=0

createdb -h 127.0.0.1 -p "$port" -U "$owner"   --template=template0 --encoding=UTF8 "$restore_db"
restore_created=1
pg_restore --exit-on-error --no-owner --no-acl -h 127.0.0.1   -p "$port" -U "$owner" -d "$restore_db" "$archive"

restored_manifest=$(psql_restore -c "$manifest_sql")
[ "$restored_manifest" = "$source_manifest" ] ||
  die 'restored schema, migrations, constraints, policies or row counts diverged'
[ "$(psql_restore -c "SELECT value FROM public.lefteria_ci_restore_canary WHERE id=1")" = 'synthetic-ci-only' ] ||
  die 'synthetic data did not survive independent restore'
[ "$(psql_restore -c "SELECT count(*) FROM pg_constraint WHERE conrelid='public.lefteria_ci_restore_canary'::regclass")" -ge 2 ] ||
  die 'synthetic row constraints were not restored'

printf '%s\n' 'PASS: isolated PostgreSQL custom backup, separate database restore, schema, migration, RLS/policy counts and synthetic data checks.'
printf '%s\n' 'No production access, no off-host archive, no go-live approval.'
