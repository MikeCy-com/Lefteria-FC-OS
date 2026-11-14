#!/usr/bin/env bash
# Preview-only: mirror public lefteriafc.cy content (GET only, <=2 req/s) into .runtime, build import
# manifests, and load them through the repository's own public-content/public-sections importers.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$HERE/workspace.conf"
RT="$LEFTERIA_RUNTIME_DIR"
set -a; source "$RT/workspace.env"; set +a
export DOTNET_ROOT="$LEFTERIA_TOOLS_DIR/dotnet" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
[[ "${SKIP_FETCH:-0}" == 1 ]] || python3 "$HERE/live_mirror.py"
python3 "$HERE/mirror_manifests.py"
python3 "$HERE/attach_mirror_media.py" "$RT/public-media"
"$DOTNET_ROOT/dotnet" build "$LEFTERIA_REPO/backend/tools/LefteriaFc.MobilePreview" -c Release --nologo -v q >/dev/null
LEFTERIA_TEST_FIXTURE="$RT/identity-db.json" LEFTERIA_WORKSPACE_STATE="$RT/state/demo-state.json" \
  "$DOTNET_ROOT/dotnet" "$LEFTERIA_REPO/backend/tools/LefteriaFc.MobilePreview/bin/Release/net10.0/LefteriaFc.MobilePreview.dll" \
  --workspace import-public "$RT/live-mirror/manifests"
