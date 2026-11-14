#!/usr/bin/env bash
# One-command deferred seed (REQ-27): adds the demo management role to the
# fictional coach/management phone. Refuses to run before the owner account exists.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$HERE/workspace.conf"
RT="$LEFTERIA_RUNTIME_DIR"
set -a; source "$RT/workspace.env"; set +a
export DOTNET_ROOT="$LEFTERIA_TOOLS_DIR/dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
LEFTERIA_TEST_FIXTURE="$RT/identity-db.json" LEFTERIA_WORKSPACE_STATE="$RT/state/demo-state.json" \
  exec "$DOTNET_ROOT/dotnet" "$LEFTERIA_REPO/backend/tools/LefteriaFc.MobilePreview/bin/Release/net10.0/LefteriaFc.MobilePreview.dll" \
  --workspace seed-management
