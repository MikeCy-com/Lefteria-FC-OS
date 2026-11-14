#!/usr/bin/env bash
# Idempotent preview-workspace bootstrap for Lefteria FC OS (never production).
#   bootstrap.sh prepare    toolchains, local secrets, PostgreSQL cluster, build
#   bootstrap.sh provision  database, migrations (--migrate) and ops/postgres roles; needs PostgreSQL running
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=workspace.conf
source "$HERE/workspace.conf"

REPO="$LEFTERIA_REPO"
# The integration-test runner points this at a throwaway runtime; the demo uses the configured one.
RT="${LEFTERIA_RUNTIME_DIR_OVERRIDE:-$LEFTERIA_RUNTIME_DIR}"
ENV_FILE="$RT/workspace.env"
FIXTURE_FILE="$RT/identity-db.json"
STATE_DIR="$RT/state"
PGDATA="$RT/pgdata"
PGSOCK="$RT/run/pg"
PGBIN="/usr/lib/postgresql/$LEFTERIA_PG_MAJOR/bin"
DOTNET_DIR="$LEFTERIA_TOOLS_DIR/dotnet"
TOOL_PROJECT="$REPO/backend/tools/LefteriaFc.MobilePreview"
API_DLL="$REPO/backend/src/LefteriaFc.Api/bin/Release/net10.0/LefteriaFc.Api.dll"
export DOTNET_ROOT="$DOTNET_DIR" PATH="$DOTNET_DIR:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
  MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false NUGET_PACKAGES="${NUGET_PACKAGES:-/root/.nuget/packages}"

log() { printf '[bootstrap %s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }

sdk_version() { python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["sdk"]["version"])' "$REPO/global.json"; }

ensure_dotnet() {
  local want; want="$(sdk_version)"
  if [ -x "$DOTNET_DIR/dotnet" ] && "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | grep -q "^$want "; then return; fi
  log "installing .NET SDK $want into $DOTNET_DIR"
  mkdir -p "$LEFTERIA_TOOLS_DIR"
  curl -fsSL https://builds.dotnet.microsoft.com/dotnet/scripts/v1/dotnet-install.sh -o "$LEFTERIA_TOOLS_DIR/dotnet-install.sh"
  bash "$LEFTERIA_TOOLS_DIR/dotnet-install.sh" --jsonfile "$REPO/global.json" --install-dir "$DOTNET_DIR" --no-path
  "$DOTNET_DIR/dotnet" --list-sdks | grep -q "^$want " || { log "SDK $want missing after install"; exit 1; }
}

ensure_postgres() {
  if [ -x "$PGBIN/postgres" ] && [ -x "$PGBIN/psql" ] && id postgres >/dev/null 2>&1; then return; fi
  log "installing PostgreSQL $LEFTERIA_PG_MAJOR from PGDG (bookworm)"
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get install -y -qq curl ca-certificates gnupg >/dev/null
  install -d /usr/share/postgresql-common/pgdg
  curl -fsSL -o /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc https://www.postgresql.org/media/keys/ACCC4CF8.asc
  echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt bookworm-pgdg main" \
    > /etc/apt/sources.list.d/pgdg.list
  apt-get update -qq
  # Packaged clusters are not used; never let apt create/start one on 5432.
  mkdir -p /etc/postgresql-common
  grep -qs '^create_main_cluster' /etc/postgresql-common/createcluster.conf || echo 'create_main_cluster = false' >> /etc/postgresql-common/createcluster.conf
  apt-get install -y -qq "postgresql-$LEFTERIA_PG_MAJOR" >/dev/null
  [ -x "$PGBIN/postgres" ] || { log "PostgreSQL binaries missing after install"; exit 1; }
}

secret_b64() { openssl rand -base64 32; }
secret_pw() { openssl rand -hex 24; }

ensure_secrets() {
  mkdir -p "$RT" "$STATE_DIR" "$RT/run" "$RT/logs"
  touch "$ENV_FILE"; chmod 600 "$ENV_FILE"
  add() { grep -q "^$1=" "$ENV_FILE" || printf '%s=%s\n' "$1" "$2" >> "$ENV_FILE"; }
  add POSTGRES_PASSWORD "$(secret_pw)"
  add DATABASE_APP_PASSWORD "$(secret_pw)"
  add DATABASE_ADMIN_AUTH_PASSWORD "$(secret_pw)"
  add MOBILE_SIGNING_KEY "$(secret_b64)"
  add MOBILE_OTP_KEY "$(secret_b64)"
  add WEB_ADMIN_THROTTLE_KEY "$(secret_b64)"
  add WEB_ADMIN_FOUNDING_SETUP_CODE "$(openssl rand -hex 32)"
}

load_secrets() { set -a; # shellcheck disable=SC1090
  source "$ENV_FILE"; set +a; }

conn() { printf 'Host=127.0.0.1;Port=%s;Database=%s;Username=%s;Password=%s' "$LEFTERIA_PG_PORT" "$LEFTERIA_PG_DATABASE" "$1" "$2"; }

ensure_fixture_file() {
  local tmp="$FIXTURE_FILE.tmp"
  ( umask 077
    FIXTURE_OWNER="$(conn "$LEFTERIA_PG_OWNER" "$POSTGRES_PASSWORD")" \
    FIXTURE_APP="$(conn "$LEFTERIA_APP_ROLE" "$DATABASE_APP_PASSWORD")" \
    FIXTURE_AUTH="$(conn "$LEFTERIA_ADMIN_AUTH_ROLE" "$DATABASE_ADMIN_AUTH_PASSWORD")" \
    python3 - "$tmp" <<'PY'
import json, os, sys
c = os.environ
doc = {'disposable': True, 'purpose': 'Lefteria rewrite identity integration tests only',
       'host': '127.0.0.1', 'port': int(c['LEFTERIA_PG_PORT']), 'database': c['LEFTERIA_PG_DATABASE'],
       'username': c['LEFTERIA_PG_OWNER'], 'connectionString': c['FIXTURE_OWNER'],
       'applicationUsername': c['LEFTERIA_APP_ROLE'], 'applicationConnectionString': c['FIXTURE_APP'],
       'adminAuthUsername': c['LEFTERIA_ADMIN_AUTH_ROLE'], 'adminAuthConnectionString': c['FIXTURE_AUTH']}
open(sys.argv[1], 'w').write(json.dumps(doc))
PY
  )
  mv "$tmp" "$FIXTURE_FILE"; chmod 600 "$FIXTURE_FILE"
}

ensure_cluster() {
  mkdir -p "$PGDATA" "$PGSOCK"
  chmod 700 "$PGDATA"
  [ "$(stat -c %U "$PGDATA")" = postgres ] || chown -R postgres:postgres "$PGDATA"
  chown postgres:postgres "$PGSOCK"
  if [ -f "$PGDATA/PG_VERSION" ]; then return; fi
  log "initializing PostgreSQL cluster in $PGDATA"
  local pw; pw="$(mktemp)"; printf '%s\n' "$POSTGRES_PASSWORD" > "$pw"; chown postgres "$pw"
  runuser -u postgres -- "$PGBIN/initdb" -D "$PGDATA" -U "$LEFTERIA_PG_OWNER" --pwfile="$pw" \
    --auth-local=scram-sha-256 --auth-host=scram-sha-256 --encoding=UTF8 --locale=C.UTF-8 >/dev/null
  rm -f "$pw"
  # Loopback only. The command line in runtime.py repeats these settings.
  cat > "$PGDATA/pg_hba.conf" <<EOF
local all all scram-sha-256
host  all all 127.0.0.1/32 scram-sha-256
EOF
  chown postgres:postgres "$PGDATA/pg_hba.conf"
  rm -f "$STATE_DIR/demo-state.json" "$STATE_DIR/demo-state.json.pending" "$STATE_DIR/grants.sha256"
}

source_stamp() {
  ( cd "$REPO" && find backend/src backend/tools backend/tests backend/Directory.Build.props global.json -type d \( -name bin -o -name obj \) -prune -o -type f -printf '%P %s %T@\n' | sort | sha256sum | cut -d' ' -f1 )
}

ensure_build() {
  local stamp="$STATE_DIR/build.sha256" now; now="$(source_stamp)"
  local dll="$TOOL_PROJECT/bin/Release/net10.0/LefteriaFc.MobilePreview.dll"
  if [ -f "$dll" ] && [ -f "$API_DLL" ] && [ "$(cat "$stamp" 2>/dev/null)" = "$now" ]; then return; fi
  log "restoring (locked) and building the workspace API host"
  (cd "$REPO" && dotnet restore backend/tools/LefteriaFc.MobilePreview --locked-mode -v quiet \
     && dotnet build backend/tools/LefteriaFc.MobilePreview -c Release --no-restore -v quiet -nologo -clp:ErrorsOnly -nodeReuse:false -p:UseSharedCompilation=false)
  source_stamp > "$stamp"
}

PSQL_ARGS=(-X -q -h 127.0.0.1 -p "$LEFTERIA_PG_PORT" -U "$LEFTERIA_PG_OWNER")
owner_psql() { PGPASSWORD="$POSTGRES_PASSWORD" "$PGBIN/psql" "${PSQL_ARGS[@]}" "$@"; }
db_exists() { [ "$(owner_psql -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$LEFTERIA_PG_DATABASE'")" = 1 ]; }

grants_stamp() {
  ( cd "$REPO" && { sha256sum ops/postgres/*.sql; ls backend/src/LefteriaFc.Infrastructure/Persistence/Migrations; } | sha256sum | cut -d' ' -f1 )
}

ops_script() {
  local file="$1"; shift
  PGPASSWORD="$POSTGRES_PASSWORD" DATABASE_APP_PASSWORD="$DATABASE_APP_PASSWORD" \
    DATABASE_ADMIN_AUTH_PASSWORD="$DATABASE_ADMIN_AUTH_PASSWORD" \
    "$PGBIN/psql" "${PSQL_ARGS[@]}" -d "$LEFTERIA_PG_DATABASE" -v ON_ERROR_STOP=1 \
      -v app_role="$LEFTERIA_APP_ROLE" -v admin_auth_role="$LEFTERIA_ADMIN_AUTH_ROLE" \
      -f "$REPO/ops/postgres/$file" "$@" >> "$RT/logs/grants.log" 2>&1
}

provision() {
  load_secrets
  if [ -f "$STATE_DIR/demo-state.json.pending" ] && [ ! -f "$STATE_DIR/demo-state.json" ] && db_exists; then
    log "previous demo seed was interrupted; recreating the disposable workspace database"
    owner_psql -d postgres -c "DROP DATABASE \"$LEFTERIA_PG_DATABASE\" WITH (FORCE)"
    rm -f "$STATE_DIR/grants.sha256"
  fi
  rm -f "$STATE_DIR/demo-state.json.pending"
  if ! db_exists; then
    log "creating database $LEFTERIA_PG_DATABASE owned by $LEFTERIA_PG_OWNER"
    owner_psql -d postgres -c "CREATE DATABASE \"$LEFTERIA_PG_DATABASE\" OWNER \"$LEFTERIA_PG_OWNER\""
    rm -f "$STATE_DIR/demo-state.json" "$STATE_DIR/grants.sha256"
  fi
  log "applying migrations through the API --migrate path"
  LEFTERIA_MIGRATION_CONNECTION="$(conn "$LEFTERIA_PG_OWNER" "$POSTGRES_PASSWORD")" dotnet "$API_DLL" --migrate > "$RT/logs/migrate.log" 2>&1 \
    || { log "migration failed; see $RT/logs/migrate.log"; exit 1; }
  local now; now="$(grants_stamp)"
  if [ "$(cat "$STATE_DIR/grants.sha256" 2>/dev/null)" = "$now" ]; then log "runtime roles and grants already current"; return; fi
  log "provisioning restricted app role, web-admin auth role and grants (ops/postgres)"
  : > "$RT/logs/grants.log"
  # Same role/grant scripts, order and permission checks as the repo's CI PostgreSQL job.
  local f
  for f in create-app-role.sql; do step "$f"; done
  check_as "$LEFTERIA_APP_ROLE" "$DATABASE_APP_PASSWORD" identity-app-permissions-check.sql
  for f in guardian-permission-grants.sql create-web-admin-auth-role.sql academy-attendance-admin-grants.sql \
    academy-training-plans-admin-grants.sql player-birthday-admin-grants.sql academy-crm-admin-grants.sql; do step "$f"; done
  check_as "$LEFTERIA_ADMIN_AUTH_ROLE" "$DATABASE_ADMIN_AUTH_PASSWORD" web-admin-auth-permissions-check.sql
  step payabl-finance-grants.sql
  echo "$now" > "$STATE_DIR/grants.sha256"
}

step() { echo "== $1" >> "$RT/logs/grants.log"; ops_script "$1" || { log "$1 failed; see $RT/logs/grants.log"; exit 1; }; }

check_as() {
  echo "== check $3 as $1" >> "$RT/logs/grants.log"
  PGPASSWORD="$2" "$PGBIN/psql" -X -q -h 127.0.0.1 -p "$LEFTERIA_PG_PORT" -U "$1" -d "$LEFTERIA_PG_DATABASE" \
    -v ON_ERROR_STOP=1 -v app_role="$LEFTERIA_APP_ROLE" -v admin_auth_role="$LEFTERIA_ADMIN_AUTH_ROLE" \
    -f "$REPO/ops/postgres/$3" >> "$RT/logs/grants.log" 2>&1 || { log "$3 failed; see $RT/logs/grants.log"; exit 1; }
}

prepare() {
  ensure_dotnet
  ensure_postgres
  ensure_secrets
  load_secrets
  export LEFTERIA_PG_PORT LEFTERIA_PG_DATABASE LEFTERIA_PG_OWNER LEFTERIA_APP_ROLE LEFTERIA_ADMIN_AUTH_ROLE
  ensure_fixture_file
  ensure_cluster
  ensure_build
}

case "${1:-}" in
  prepare) prepare ;;
  provision) provision ;;
  *) echo "usage: $0 prepare|provision" >&2; exit 2 ;;
esac
