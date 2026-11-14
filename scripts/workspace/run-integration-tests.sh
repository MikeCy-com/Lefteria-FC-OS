#!/usr/bin/env bash
# Runs the repo's PostgreSQL integration suite (same steps as the CI "identity" job,
# except the EF down/up drill) against a THROWAWAY cluster on the guarded fixture
# address 127.0.0.1:54329. The persisted demo cluster is never touched: stop the
# backend service first so port 54329 is free (supervisorctl stop backend).
# Also verifies the workspace host's deferred management seed on the throwaway DB.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$HERE/workspace.conf"
REPO="$LEFTERIA_REPO"
IT="${LEFTERIA_IT_DIR:-/tmp/lefteria-it}"
PGBIN="/usr/lib/postgresql/$LEFTERIA_PG_MAJOR/bin"
export PATH="$LEFTERIA_TOOLS_DIR/dotnet:$PATH" DOTNET_ROOT="$LEFTERIA_TOOLS_DIR/dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 \
  MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0

if (exec 3<>/dev/tcp/127.0.0.1/"$LEFTERIA_PG_PORT") 2>/dev/null; then
  echo "Port $LEFTERIA_PG_PORT is in use (demo PostgreSQL). Run: supervisorctl stop backend" >&2; exit 2
fi
rm -rf "$IT"; mkdir -p "$IT"
export LEFTERIA_RUNTIME_DIR_OVERRIDE="$IT"
bash "$HERE/bootstrap.sh" prepare || exit 1
touch "$IT/logs/postgres.log"; chown postgres "$IT/logs/postgres.log"
runuser -u postgres -- "$PGBIN/pg_ctl" -D "$IT/pgdata" -l "$IT/logs/postgres.log" -w start \
  -o "-p $LEFTERIA_PG_PORT -c listen_addresses=127.0.0.1 -c unix_socket_directories=$IT/run/pg" >/dev/null || exit 1
cleanup() { runuser -u postgres -- "$PGBIN/pg_ctl" -D "$IT/pgdata" -m fast -w stop >/dev/null 2>&1; kill "${HOSTPID:-0}" 2>/dev/null; }
trap cleanup EXIT
bash "$HERE/bootstrap.sh" provision || exit 1

set -a; source "$IT/workspace.env"; set +a
export LEFTERIA_TEST_FIXTURE="$IT/identity-db.json"
export LEFTERIA_MIGRATION_CONNECTION="Host=127.0.0.1;Port=$LEFTERIA_PG_PORT;Database=$LEFTERIA_PG_DATABASE;Username=$LEFTERIA_PG_OWNER;Password=$POSTGRES_PASSWORD"
pass=0; fail=0; failed=()
run() { local name="$1"; shift; echo "### $name"; if "$@"; then pass=$((pass+1)); echo "RESULT PASS $name"; else fail=$((fail+1)); failed+=("$name"); echo "RESULT FAIL $name"; fi; }
sql() { PGPASSWORD="$POSTGRES_PASSWORD" "$PGBIN/psql" -X -q -h 127.0.0.1 -p "$LEFTERIA_PG_PORT" -U "$LEFTERIA_PG_OWNER" -d "$LEFTERIA_PG_DATABASE" \
  -v ON_ERROR_STOP=1 -v app_role="$LEFTERIA_APP_ROLE" -v admin_auth_role="$LEFTERIA_ADMIN_AUTH_ROLE" -f "$REPO/ops/postgres/$1" >/dev/null; }

for f in founding-admin-checks.sql academy-private-evaluation-guards-checks.sql private-document-quarantine-checks.sql \
  pos-location-ledger-checks.sql pos-cashier-ledger-checks.sql hub-recipient-learning-checks.sql academy-attendance-checks.sql \
  academy-attendance-lifecycle-checks.sql academy-training-plans-checks.sql academy-training-plans-lifecycle-checks.sql \
  player-birthday-lifecycle-checks.sql academy-crm-inbox-checks.sql academy-registration-intake-security-checks.sql \
  academy-office-checks.sql payabl-callback-state-checks.sql payabl-training-fee-checks.sql general-charge-private-ledger-checks.sql \
  admin-academy-profile-boundary.sql admin-first-team-profile-boundary.sql admin-profiled-group-create-boundary.sql guardian-permission-boundary.sql; do
  run "sql:$f" sql "$f"
done

T=backend/tests/LefteriaFc.Identity.IntegrationTests
cd "$REPO"
run "restore integration tests (locked)" dotnet restore "$T" --locked-mode -v quiet
run "build integration tests" dotnet build "$T" -c Release --no-restore -v quiet -nologo -clp:ErrorsOnly -nodeReuse:false
for mode in --web-admin-password-policy --web-admin-configuration-policy --payabl-adapter-policy --public-fixture-clock-policy; do
  run "integration $mode" dotnet run --project "$T" -c Release --no-build -- "$mode"
done
run "integration suite (PostgreSQL)" dotnet run --project "$T" -c Release --no-build
P=backend/tools/LefteriaFc.MobilePreview
run "MobilePreview --smoke" dotnet run --project "$P" -c Release --no-build -- --smoke --port 4191
run "MobilePreview --calendar --smoke" dotnet run --project "$P" -c Release --no-build -- --calendar --smoke --port 4192
run "MobilePreview --web-platform --smoke" dotnet run --project "$P" -c Release --no-build -- --web-platform --smoke --port 4193

# Workspace host on the throwaway DB: seed, founding setup with THIS run's code, deferred management seed.
workspace_flow() {
  local port=5181 origin="$LEFTERIA_PUBLIC_ORIGIN" dll="$REPO/$P/bin/Release/net10.0/LefteriaFc.MobilePreview.dll"
  local state="$IT/state/demo-state.json" j='Content-Type: application/json'
  LEFTERIA_WORKSPACE_STATE="$state" LEFTERIA_WORKSPACE_API_PORT=$port LEFTERIA_WORKSPACE_PUBLIC_ORIGIN="$origin" \
    LEFTERIA_WORKSPACE_KNOWN_PROXY=127.0.0.1 dotnet "$dll" --workspace serve > "$IT/logs/workspace-host.log" 2>&1 &
  HOSTPID=$!
  for _ in $(seq 1 90); do curl -sf "http://127.0.0.1:$port/api/health/ready" >/dev/null && break; sleep 1; done
  local staff; staff="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["StaffPhone"])' "$state")" || return 1
  LEFTERIA_WORKSPACE_STATE="$state" bash -c "exec dotnet '$dll' --workspace seed-management" && { echo "seed-management ran before owner setup"; return 1; }
  local h=(-H "$j" -H 'X-Lefteria-Admin: 1' -H "Origin: $origin" -H 'X-Forwarded-Proto: https')
  [ "$(curl -s -o /dev/null -w '%{http_code}' "${h[@]}" -X POST "http://127.0.0.1:$port/api/admin/auth/setup" \
    -d "{\"username\":\"it-owner\",\"name\":\"IT owner\",\"password\":\"throwaway-owner-pass\",\"setup_code\":\"$WEB_ADMIN_FOUNDING_SETUP_CODE\"}")" = 204 ] || return 1
  LEFTERIA_WORKSPACE_STATE="$state" dotnet "$dll" --workspace seed-management || return 1
  curl -sf -H "$j" -X POST "http://127.0.0.1:$port/api/mobile/auth/request-otp" -d "{\"phone\":\"$staff\"}" >/dev/null || return 1
  curl -s -H "$j" -X POST "http://127.0.0.1:$port/api/mobile/auth/verify-otp" -d "{\"phone\":\"$staff\",\"code\":\"123456\"}" | tee "$IT/logs/staff-picker.json" | grep -q '"management"'
}
run "workspace host: deferred management seed after owner setup (throwaway DB)" workspace_flow

echo "SUMMARY passed=$pass failed=$fail"
for name in "${failed[@]}"; do echo "FAILED: $name"; done
[ "$fail" = 0 ]
