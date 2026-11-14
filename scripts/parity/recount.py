#!/usr/bin/env python3
"""Independent recount for the parity matrices (Q-9, Q-10, Q-11). Exit 1 on any gap."""
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
LEGACY = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / ".runtime/legacy-source"
DECO = re.compile(r"^[ \t]*@\w+\.(get|post|put|patch|delete|head|options|api_route)\(", re.M)

ops = json.loads((REPO / "docs/parity/api-matrix.json").read_text())["operations"]
screens = json.loads((REPO / "docs/parity/ui-matrix.json").read_text())["screens"]
openapi = json.loads((REPO / "contracts/openapi.json").read_text())["paths"]
failures = []

sources = [LEGACY / "backend/server.py"] + sorted((LEGACY / "backend/routes").glob("*.py"))
expected = {}
for f in sources:
    rel = str(f.relative_to(LEGACY / "backend"))
    text = f.read_text()
    for m in DECO.finditer(text):
        expected[f"{rel}:{text.count(chr(10), 0, m.start()) + 1}"] = m.group(1)
covered = {o["id"].rsplit(":", 1)[0] for o in ops}
missing = sorted(set(expected) - covered)
print(f"Q-9  decorators={len(expected)} in_matrix={len(set(expected) & covered)} coverage={100 * len(set(expected) & covered) / len(expected):.1f}%")
failures += [f"decorator not in matrix: {m}" for m in missing]

statuses = {"ported", "partial", "missing", "deprecated-by-design", "provider-gated"}
for o in ops:
    if o["os_status"] not in statuses:
        failures.append(f"bad status {o['id']}")
    if o["os_status"] in ("missing", "partial") and (o.get("target_phase") not in (3, 4, 5) or not o.get("module_group")):
        failures.append(f"no phase/module {o['id']}")
false_ported = []
for o in ops:
    if o["os_status"] == "ported":
        method, path = o["os_endpoint"].split(" ", 1)
        if method.lower() not in openapi.get(path, {}):
            false_ported.append(o["id"])
print(f"Q-11 ported={sum(o['os_status'] == 'ported' for o in ops)} false_ported={len(false_ported)}")
failures += [f"false ported {x}" for x in false_ported]

front = LEGACY / "frontend/src"
routes = set(re.findall(r'<Route\s+path="([^"]+)"', (front / "App.js").read_text()))
admin = {f.stem for f in (front / "pages/admin").glob("*.jsx")}
mobile = {f.stem for f in (front / "mobile/pages").glob("*.jsx") if ".test." not in f.name}
have = {s["id"] for s in screens}
gaps = [f"route:{r}" for r in routes if f"route:{r}" not in have] + [f"admin-module:{a}" for a in admin if f"admin-module:{a}" not in have] + \
       [f"mobile-page:{m}" for m in mobile if f"mobile-page:{m}" not in have]
total = len(routes) + len(admin) + len(mobile)
print(f"Q-10 routes={len(routes)} admin_modules={len(admin)} mobile_pages={len(mobile)} coverage={100 * (total - len(gaps)) / total:.1f}%")
failures += [f"screen not in matrix: {g}" for g in gaps]

for line in failures[:40]:
    print("FAIL", line)
sys.exit(1 if failures else 0)
