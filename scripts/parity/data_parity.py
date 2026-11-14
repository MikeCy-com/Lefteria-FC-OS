#!/usr/bin/env python3
"""Phase 3A data-parity evidence (Q-18): live mirror counts vs the OS public API for both public
organizations, every live player and academy group page, and mirrored public media delivery.
Reads only the local preview; writes /app/.runtime/parity/data-parity.json (git-ignored)."""
import json
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CONF = dict(l.split("=", 1) for l in (REPO / "scripts/workspace/workspace.conf").read_text().split() if "=" in l)
API = "http://127.0.0.1:8001"
WEB = "http://127.0.0.1:3000"
MIRROR = Path("/app/.runtime/live-mirror/manifests/public-sections.json")
OUT = Path("/app/.runtime/parity/data-parity.json")
ORGS = ("lefteria_fc", "lefteria_fc_academy")


def get(url):
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            return response.status, response.headers, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.headers, b""


def ids(rows):
    return {row["id"] for row in rows}


def main():
    live = json.loads(MIRROR.read_text())
    os_sections = {org: json.loads(get(f"{API}/api/public/sections?organization_id={org}")[2]) for org in ORGS}
    os_site = {org: json.loads(get(f"{API}/api/public/site?organization_id={org}")[2]) for org in ORGS}
    union = lambda key: set().union(*(ids(os_sections[org][key]) for org in ORGS))
    checks = {}

    def compare(name, live_rows, os_ids):
        expected = ids(live_rows)
        checks[name] = {"live": len(expected), "os": len(os_ids & expected), "missing": len(expected - os_ids)}

    compare("players", live["players"], union("players"))
    compare("academy_groups", live["academy_groups"], set().union(*(ids(os_site[org]["academy_groups"]) for org in ORGS)))
    compare("partners", live["partners"], union("partners"))
    compare("sponsors", live["sponsors"], union("sponsors"))
    compare("standings", live["standings"], union("standings"))
    compare("venues", live["venues"], union("venues"))
    compare("seasons", live["seasons"], union("seasons"))
    compare("archived_seasons", live["archived_seasons"], union("archived_seasons"))
    compare("teams", live["teams"], set().union(*(ids(os_site[org]["teams"]) for org in ORGS)))
    compare("staff", live["staff"], union("staff"))
    checks["club_profile"] = {"live": len(live["club_profile"]), "os": int(os_site["lefteria_fc"].get("club") is not None), "missing": 0}
    pages = {"player": [f"/player/{row['id']}" for row in live["players"]],
             "academy_group": [f"/academy/{row['id']}" for row in live["academy_groups"]]}
    for kind, paths in pages.items():
        failures = [path for path in paths if get(WEB + path)[0] != 200]
        checks[f"{kind}_pages"] = {"live": len(paths), "os": len(paths) - len(failures), "missing": len(failures)}
    media = sorted({urllib.parse.urlparse(row["image_url"]).path for row in live["players"] if (row.get("image_url") or "").startswith("/api/uploads/")})
    bad = []
    for path in media:
        status, headers, body = get(API + urllib.parse.quote(path))
        if status != 200 or not headers.get("Content-Type", "").startswith("image/") or "immutable" not in headers.get("Cache-Control", "") or not body:
            bad.append(path)
    checks["player_media"] = {"live": len(media), "os": len(media) - len(bad), "missing": len(bad)}
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(checks, indent=1))
    for name, row in checks.items():
        print(f"{'PASS' if row['missing'] == 0 else 'FAIL'} {name}: live={row['live']} os={row['os']}")
    return 1 if any(row["missing"] for row in checks.values()) else 0


if __name__ == "__main__":
    sys.exit(main())
