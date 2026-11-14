#!/usr/bin/env python3
"""Builds docs/parity/api-matrix.json, api-matrix.md and ui-matrix.json from the read-only
original source (/app/.runtime/legacy-source) and this repo (contracts/openapi.json,
frontend/src). Offline only; never calls a live service. Usage: build_matrices.py [legacy_dir]"""
import ast
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
LEGACY = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / ".runtime/legacy-source"
BACK = LEGACY / "backend"
FRONT = LEGACY / "frontend/src"
OUT = REPO / "docs/parity"
METHODS = ("get", "post", "put", "patch", "delete", "head", "options", "api_route")
OS_PATHS = json.loads((REPO / "contracts/openapi.json").read_text())["paths"]


def norm(path):
    path = re.sub(r"\{[^}]+\}", "{}", path.rstrip("/") or "/")
    return re.sub(r"/+", "/", path)


OS_INDEX = defaultdict(dict)
for os_path, ops in OS_PATHS.items():
    for method in ops:
        if method in METHODS:
            OS_INDEX[norm(os_path)][method.upper()] = os_path


def const(node):
    return node.value if isinstance(node, ast.Constant) and isinstance(node.value, str) else None


def kw(call, name):
    for k in call.keywords:
        if k.arg == name:
            return k.value
    return None


def source_files():
    return [BACK / "server.py"] + sorted((BACK / "routes").glob("*.py"))


# ---------- Pydantic models (request field lookup) ----------
MODELS = {}
for f in [*BACK.glob("*.py"), *(BACK / "routes").glob("*.py")]:
    try:
        tree = ast.parse(f.read_text())
    except SyntaxError:
        continue
    for node in ast.walk(tree):
        if isinstance(node, ast.ClassDef):
            fields = [s.target.id for s in node.body if isinstance(s, ast.AnnAssign) and isinstance(s.target, ast.Name)]
            if fields:
                MODELS.setdefault(node.name, fields)

# ---------- include_router prefixes from server.py ----------
server_tree = ast.parse((BACK / "server.py").read_text())
imports = {}
for node in ast.walk(server_tree):
    if isinstance(node, ast.ImportFrom) and node.module and node.module.startswith("routes."):
        for alias in node.names:
            imports[alias.asname or alias.name] = (node.module.split(".", 1)[1], alias.name)
assigned = {}  # names assigned from an imported factory call, e.g. a, b = setup_x(...)
for node in ast.walk(server_tree):
    if isinstance(node, ast.Assign) and isinstance(node.value, ast.Call) and isinstance(node.value.func, ast.Name) and node.value.func.id in imports:
        for target in node.targets:
            for name in (target.elts if isinstance(target, ast.Tuple) else [target]):
                if isinstance(name, ast.Name):
                    assigned[name.id] = imports[node.value.func.id][0]
includes = defaultdict(list)  # (module, router var or "*factory*") -> [prefix]
for node in ast.walk(server_tree):
    if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) and node.func.attr == "include_router" and node.args:
        arg, prefix = node.args[0], const(kw(node, "prefix")) or ""
        if isinstance(node.func.value, ast.Name) and node.func.value.id == "api_router":
            prefix = "/api" + prefix
        name = arg.id if isinstance(arg, ast.Name) else (arg.func.id if isinstance(arg, ast.Call) and isinstance(arg.func, ast.Name) else
                                                          arg.func.attr if isinstance(arg, ast.Call) and isinstance(arg.func, ast.Attribute) else None)
        if name in imports:
            module, original = imports[name]
            includes[(module, "*factory*" if isinstance(arg, ast.Call) else original)].append(prefix)
        elif isinstance(arg, ast.Name) and name in assigned:
            includes[(assigned[name], "*factory*")].append(prefix)
        elif isinstance(arg, ast.Name):
            includes[("server", name)].append(prefix)
        elif isinstance(arg, ast.Call):
            for module, original in imports.values():
                if original == name:
                    includes[(module, "*factory*")].append(prefix)

ROLE_WORDS = {"super_admin", "manager", "coach", "parent", "player", "fan", "management", "matchday_editor", "marketing",
              "academy_director", "accountant", "editor", "cashier", "staff", "admin", "treasurer", "secretary"}
SCOPE_HELPERS = re.compile(r"\b(organization_scope|org_scope|scope_[a-z_]+|[a-z_]*scope[a-z_]*|family_access|can_access_[a-z_]+|"
                           r"assert_[a-z_]*access[a-z_]*|organization_id|team_id|academy_group_id|linked_player_ids?)\b")
WRITE = re.compile(r"db\.([A-Za-z_]+)\.(insert_one|insert_many|update_one|update_many|delete_one|delete_many|"
                   r"find_one_and_update|find_one_and_delete|replace_one|bulk_write)")
EXTERNAL = re.compile(r"\b(twilio|stripe|webpush|pywebpush|vapid|openai|LlmChat|emergentintegrations|facebook|instagram|tiktok|"
                      r"youtube|send_email|smtp|resend|sendgrid|sms|send_push|google|s3|boto3|httpx|requests\.)", re.I)
PROVIDER = re.compile(r"twilio|stripe|webpush|vapid|openai|LlmChat|emergentintegrations|facebook|instagram|tiktok|oauth|sms|"
                      r"send_push|push/subscribe|vapid|resend|sendgrid|smtp", re.I)
DEPRECATED = re.compile(r"/(seed|debug|test-|_test|dev-|demo-seed|reset-demo)|/api/legacy", re.I)


GUARDED_ROUTERS = {}  # (file stem, router var) -> dependency names declared on APIRouter(dependencies=[...])
SECTIONS = re.compile(r"^/api/(teams|players|staff|sponsors|partners|events|venues|standings|seasons|gallery|fixtures|academy/groups)(/|$)")


def router_prefixes(tree, stem=""):
    result = {}
    for node in ast.walk(tree):
        if isinstance(node, ast.Assign) and isinstance(node.value, ast.Call) and getattr(node.value.func, "id", "") == "APIRouter":
            deps = kw(node.value, "dependencies")
            if deps is not None:
                for target in node.targets:
                    if isinstance(target, ast.Name):
                        GUARDED_ROUTERS[(stem, target.id)] = ast.unparse(deps)
    for node in ast.walk(tree):
        if isinstance(node, ast.Assign) and isinstance(node.value, ast.Call):
            func = node.value.func
            if (isinstance(func, ast.Name) and func.id == "APIRouter") or (isinstance(func, ast.Attribute) and func.attr == "APIRouter"):
                for target in node.targets:
                    if isinstance(target, ast.Name):
                        result[target.id] = const(kw(node.value, "prefix")) or ""
    return result


def mounted(module, var, prefix):
    if module == "server":
        if var == "app":
            return [""]
        return includes.get(("server", var), [""]) if var != "api_router" else ["/api"]
    explicit = includes.get((module, var))
    if explicit is None:
        explicit = includes.get((module, "*factory*"), [""])
    paths = [p + prefix for p in explicit] or [prefix]
    # Modules that mount their own (or nested) routers under /api inside a setup function.
    if 'prefix="/api"' in (BACK / "routes" / f"{module}.py").read_text():
        paths = [p if p.startswith("/api") else "/api" + p for p in paths]
    return paths


def analyse(fn, src_lines, deco):
    body = "\n".join(src_lines[fn.lineno - 1: fn.end_lineno])
    deps, fields, query = [], [], []
    args = fn.args.args + fn.args.kwonlyargs
    defaults = [None] * (len(fn.args.args) - len(fn.args.defaults)) + list(fn.args.defaults) + list(fn.args.kw_defaults)
    for arg, default in zip(args, defaults):
        ann = ast.unparse(arg.annotation) if arg.annotation else ""
        if isinstance(default, ast.Call) and getattr(default.func, "id", "") == "Depends":
            deps.append(ast.unparse(default.args[0]) if default.args else ann)
        elif ann in MODELS:
            fields += [f"{ann}.{name}" for name in MODELS[ann]]
        elif arg.arg not in ("request", "response", "self", "background_tasks", "db") and ann not in ("Request", "Response", "BackgroundTasks"):
            query.append(arg.arg)
    calls = set(re.findall(r"\b(get_current_user|get_mobile_user|require_permission\(\s*[\"'][^\"']+[\"']|has_permission\([^)]*\)|"
                           r"require_[a-z_]+|verify_[a-z_]*admin[a-z_]*|_require_[a-z_]+|get_optional_[a-z_]+)", body))
    deps += sorted(c for c in calls if c not in deps)
    dep_text = " ".join(deps)
    if re.search(r"mobile", dep_text):
        auth = "mobile-jwt"
    elif re.search(r"current_user|permission|require_|admin", dep_text):
        auth = "admin-jwt"
    elif re.search(r"signature|webhook|hmac", body, re.I):
        auth = "signed-callback"
    else:
        auth = "public"
    if "request.json()" in body:
        fields.append("raw-json-body")
    perms = sorted(set(re.findall(r"""(?:require_permission|has_permission)\([^"']*["']([a-z_]+:[a-z_*]+)["']""", body)))
    roles = sorted({w for w in re.findall(r"[\"']([a-z_]+)[\"']", body) if w in ROLE_WORDS})
    returns = re.findall(r"return\s*\{\s*[\"']([a-zA-Z_]+)[\"']", body)
    response = ast.unparse(kw(deco, "response_model")) if kw(deco, "response_model") else (
        "object{" + ",".join(dict.fromkeys(returns)) + "}" if returns else "json")
    writes = sorted({f"{c}.{op}" for c, op in WRITE.findall(body)})
    external = sorted({m.lower() for m in EXTERNAL.findall(body)})
    return {"auth": auth, "dependencies": deps, "permissions": perms, "roles_referenced": roles,
            "scope": sorted(set(SCOPE_HELPERS.findall(body)))[:12], "request_fields": fields, "query_or_path_params": query,
            "response": response, "side_effects": {"db_writes": writes, "external": external}, "_body": body}


def classify(method, paths, info):
    for path in paths:
        hit = OS_INDEX.get(norm(path), {})
        if method in hit:
            return "ported", f"{method} {hit[method]}", "exact OS method and path in contracts/openapi.json"
    for path in paths:
        rest = re.sub(r"^/api", "", path)
        namespaces = {"admin-jwt": ["/api/admin"], "mobile-jwt": ["/api/mobile"]}.get(info["auth"], ["/api/public"])
        for alt in [ns + rest for ns in namespaces]:
            hit = OS_INDEX.get(norm(alt), {})
            if method in hit:
                return "partial", f"{method} {hit[method]}", "equivalent OS endpoint under a new namespace; contract not proven identical"
        hit = OS_INDEX.get(norm(path), {})
        if hit:
            other = next(iter(hit))
            return "partial", f"{other} {hit[other]}", "OS path exists with a different method"
    if method == "GET" and info["auth"] == "public" and any(SECTIONS.search(p) for p in paths):
        return "partial", "GET /api/public/sections", "read model covered by the OS typed public sections projection; detail/filters differ"
    text = " ".join(paths) + " " + " ".join(info["side_effects"]["external"]) + " " + info["_body"][:4000]
    if DEPRECATED.search(" ".join(paths)):
        return "deprecated-by-design", None, "seed/debug/test-only endpoint; not carried into the rewrite"
    if PROVIDER.search(text):
        return "provider-gated", None, "needs an external provider (built OFF until keys are configured)"
    return "missing", None, "no OS endpoint yet"


def phase_for(paths, auth):
    path = paths[0]
    if auth == "mobile-jwt" or path.startswith("/api/mobile") or path.startswith("/api/m/"):
        return 5
    if auth in ("admin-jwt",) or path.startswith("/api/admin"):
        return 4
    return 3


def build_api():
    entries = []
    for f in source_files():
        module = "server" if f.name == "server.py" else f.stem
        text = f.read_text()
        lines = text.splitlines()
        tree = ast.parse(text)
        prefixes = router_prefixes(tree, module)
        guarded_file = any(k[0] == module for k in GUARDED_ROUTERS)
        for fn in ast.walk(tree):
            if not isinstance(fn, (ast.FunctionDef, ast.AsyncFunctionDef)):
                continue
            for deco in fn.decorator_list:
                if not (isinstance(deco, ast.Call) and isinstance(deco.func, ast.Attribute) and deco.func.attr in METHODS
                        and isinstance(deco.func.value, ast.Name)):
                    continue
                var = deco.func.value.id
                raw = const(deco.args[0]) if deco.args else const(kw(deco, "path")) or ""
                methods = [deco.func.attr.upper()]
                if deco.func.attr == "api_route":
                    m = kw(deco, "methods")
                    methods = [const(e).upper() for e in m.elts] if isinstance(m, ast.List) else ["GET"]
                paths = [p + raw for p in mounted(module, var, prefixes.get(var, ""))]
                info = analyse(fn, lines, deco)
                if info["auth"] == "public" and (guarded_file and "include_router" in text and "dependencies=[Depends(" in text
                                                 and any(p.startswith("/api/admin") for p in paths) or (module, var) in GUARDED_ROUTERS):
                    info["auth"] = "mobile-jwt" if "mobile" in GUARDED_ROUTERS.get((module, var), "") else "admin-jwt"
                    info["dependencies"].append("router-level " + GUARDED_ROUTERS.get((module, var), "guarded parent router"))
                for method in methods:
                    status, os_endpoint, reason = classify(method, paths, info)
                    group = module if module != "server" else (re.sub(r"^/api/?", "", paths[0]).split("/")[0] or "root")
                    info_out = {k: v for k, v in info.items() if k != "_body"}
                    entries.append({"id": f"{f.relative_to(BACK)}:{deco.lineno}:{method}", "source": f"backend/{f.relative_to(BACK)}#L{deco.lineno}",
                                    "handler": fn.name, "method": method, "path": paths[0], "mounted_paths": paths, **info_out,
                                    "os_status": status, "os_endpoint": os_endpoint, "status_reason": reason,
                                    "target_phase": phase_for(paths, info["auth"]), "module_group": group})
    return entries


# ---------- UI ----------
API_LITERAL = re.compile(r"(?:\$\{API\}|\$\{API_URL\}|\$\{BACKEND_URL\}/api|\$\{API_BASE\}|[\"'`]/api)(/[^\"'`\s)?]*)")


def api_calls(text, ops_by_norm):
    found = []
    for raw in API_LITERAL.findall(text):
        candidate = norm("/api" + re.sub(r"\$\{[^}]+\}", "{}", raw))
        for op in ops_by_norm.get(candidate, []):
            if op not in found:
                found.append(op)
    return found


def os_route_exists(path):
    base = REPO / "frontend/src/routes"
    parts = [p for p in path.strip("/").split("/") if p and p != "*"]
    current = base
    for part in parts:
        if part.startswith(":"):
            dirs = [d for d in current.iterdir() if d.is_dir() and d.name.startswith("[")] if current.exists() else []
            if not dirs:
                return None
            current = dirs[0]
        else:
            current = current / part
    for name in ("+page.svelte", "+page.server.ts", "+server.ts"):
        if (current / name).exists():
            return str((current / name).relative_to(REPO))
    return None


def build_ui(ops):
    ops_by_norm = defaultdict(list)
    for op in ops:
        for p in op["mounted_paths"]:
            ops_by_norm[norm(p)].append(op["id"])
    screens = []
    app = (FRONT / "App.js").read_text()
    for lineno, line in enumerate(app.splitlines(), 1):
        m = re.search(r'<Route\s+path="([^"]+)"\s+element=\{(.*)', line)
        if not m:
            continue
        path, element = m.groups()
        comps = [c for c in re.findall(r"<([A-Z][A-Za-z0-9]+)", element) if c not in ("PublicLayout", "ProtectedRoute", "MobileAppShell", "MobileAuthProvider", "Navigate")]
        surface = "admin" if path.startswith("/admin") else "mobile" if path.startswith("/app") else "pos" if path.startswith("/pos") else "public"
        comp = comps[-1] if comps else ("redirect" if "Navigate" in element else None)
        comp_file = next(iter(FRONT.rglob(f"{comp}.jsx")), None) if comp else None
        os_file = os_route_exists(path)
        screens.append({"id": f"route:{path}", "kind": "route", "surface": surface, "original": path, "component": comp,
                        "source": f"frontend/src/App.js#L{lineno}", "os_route_or_component": os_file,
                        "os_status": "partial" if os_file else "missing",
                        "status_note": "OS route exists; visual/content parity tracked in visual-baseline.md" if os_file else "no OS route",
                        "api_operations": api_calls(comp_file.read_text(), ops_by_norm) if comp_file else [],
                        "visibility": "authenticated" if "ProtectedRoute" in element or surface in ("admin", "mobile") else "public"})
    panel = (FRONT / "pages/AdminPanel.jsx").read_text()
    cases = dict(re.findall(r'case "([a-z_0-9]+)":[^<]*<([A-Z][A-Za-z0-9]+)', panel))
    sidebar = []
    block = panel[panel.index("const sidebarConfig = ["):]
    block = block[:block.index("\n  ];")]
    group_roles, group_orgs = None, []
    for line in block.splitlines():
        roles = re.search(r"roles: \[([^\]]*)\]", line)
        orgs = re.search(r"organizations: \[([^\]]*)\]", line)
        if 'type: "group"' in line:
            group_roles = re.findall(r'"([^"]+)"', roles.group(1)) if roles else None
            group_orgs = re.findall(r'"([^"]+)"', orgs.group(1)) if orgs else []
            continue
        if not line.startswith("      ") and 'type: "item"' in line:
            group_roles, group_orgs = None, []
        for tab, label in re.findall(r'id: "([a-z_0-9]+)", label: "([^"]+)"', line):
            sidebar.append((tab, label, re.findall(r'"([^"]+)"', roles.group(1)) if roles else (group_roles or ["any admin"]),
                            re.findall(r'"([^"]+)"', orgs.group(1)) if orgs else group_orgs))
    listed = {t for t, *_ in sidebar}
    sidebar += [(tab, tab, ["any admin (switch case only)"], []) for tab in cases if tab not in listed]
    os_admin = {d.name for d in (REPO / "frontend/src/routes/admin").iterdir() if d.is_dir()}
    alias = {"teams": "groups", "players_crm": "groups", "settings_club": "club", "news": "news", "trials": "trials", "pos": "pos",
             "attendance": "attendance", "calendar": "schedule", "registrations": "academy-office", "messages": "announcements",
             "settings_charges": "finance", "charges": "finance", "academy": "groups", "marketing": "leads"}
    tab_of_component = defaultdict(list)
    for tab, comp in cases.items():
        tab_of_component[comp].append(tab)
    for tab, label, roles, orgs in sidebar:
        target = alias.get(tab, tab.replace("_", "-"))
        comp = cases.get(tab)
        comp_file = next(iter((FRONT / "pages").rglob(f"{comp}.jsx")), None) if comp else None
        screens.append({"id": f"admin-tab:{tab}", "kind": "admin-tab", "surface": "admin", "original": tab, "label": label, "component": comp,
                        "source": "frontend/src/pages/AdminPanel.jsx", "os_route_or_component": f"frontend/src/routes/admin/{target}" if target in os_admin else None,
                        "os_status": "partial" if target in os_admin else "missing", "visibility": {"roles": roles, "organizations": orgs},
                        "api_operations": api_calls(comp_file.read_text(), ops_by_norm) if comp_file else api_calls(panel, {})})
    for f in sorted((FRONT / "pages/admin").glob("*.jsx")):
        comp = f.stem
        tabs = tab_of_component.get(comp, [])
        side = [s for s in sidebar if s[0] in tabs]
        target = next((alias.get(t, t.replace("_", "-")) for t in tabs if alias.get(t, t.replace("_", "-")) in os_admin), None)
        screens.append({"id": f"admin-module:{comp}", "kind": "admin-module", "surface": "admin", "original": comp,
                        "source": f"frontend/src/pages/admin/{f.name}", "rendered_by_tabs": tabs,
                        "os_route_or_component": f"frontend/src/routes/admin/{target}" if target else None,
                        "os_status": "partial" if target else "missing",
                        "visibility": {"roles": sorted({r for s in side for r in s[2]}) or ["inherits parent tab / any admin"],
                                       "organizations": sorted({o for s in side for o in s[3]})},
                        "api_operations": api_calls(f.read_text(), ops_by_norm)})
    mobile_app = (FRONT / "mobile/MobileApp.jsx").read_text()
    os_mobile = {f.stem for f in (REPO / "frontend/src/lib/mobile/components").glob("*.svelte")}
    mobile_alias = {"ParentDashboard": "MobileParentHome", "CoachDashboard": "MobileRoleHome", "PlayerDashboard": "MobileRoleHome",
                    "FanDashboard": "MobileRoleHome", "ManagementDashboard": "MobileRoleHome", "SchedulePage": "MobileCalendar",
                    "ProfilePage": "MobileProfile", "RoleProfilePage": "MobileProfile", "ChatPage": "MobileGroupChat",
                    "FinancialPage": "MobileTrainingFees", "FamilySettingsPage": "MobileFamily", "EducationHubPage": "MobilePublishedHubEducation",
                    "MobileLoginPage": "MobileLogin", "PlayerDevelopmentPage": "MobilePlayerCoachFeedback", "UnderDevelopmentPage": "MobileShell"}
    for f in sorted((FRONT / "mobile/pages").glob("*.jsx")):
        if ".test." in f.name:
            continue
        comp = f.stem
        roles = sorted(set(re.findall(rf'role === "([a-z]+)"[^\n]*<{comp}\b', mobile_app)))
        tabs = sorted(set(re.findall(rf'activeTab === "([a-z\-]+)"[^\n]*<{comp}\b', mobile_app)))
        target = mobile_alias.get(comp)
        screens.append({"id": f"mobile-page:{comp}", "kind": "mobile-page", "surface": "mobile", "original": comp,
                        "source": f"frontend/src/mobile/pages/{f.name}", "os_route_or_component": f"frontend/src/lib/mobile/components/{target}.svelte" if target in os_mobile else None,
                        "os_status": "partial" if target in os_mobile else "missing",
                        "visibility": {"roles": roles or ["any signed-in member (see MobileApp.jsx)"], "tabs": tabs},
                        "api_operations": api_calls(f.read_text(), ops_by_norm)})
    for role, comp in (("fan", "FanDashboard"), ("player", "PlayerDashboard"), ("parent", "ParentDashboard"),
                       ("coach", "CoachDashboard"), ("management", "ManagementDashboard")):
        screens.append({"id": f"role-dashboard:{role}", "kind": "role-dashboard", "surface": "mobile", "original": comp,
                        "source": f"frontend/src/mobile/pages/{comp}.jsx", "os_route_or_component": "frontend/src/lib/mobile/components/MobileRoleHome.svelte",
                        "os_status": "partial", "visibility": {"roles": [role]}, "api_operations": next((s["api_operations"] for s in screens if s["id"] == f"mobile-page:{comp}"), [])})
    pos = FRONT / "pos"
    for f in sorted(pos.rglob("*.jsx")) if pos.exists() else []:
        if ".test." in f.name:
            continue
        screens.append({"id": f"pos:{f.stem}", "kind": "pos-screen", "surface": "pos", "original": f.stem, "source": f"frontend/src/{f.relative_to(FRONT)}",
                        "os_route_or_component": "frontend/src/routes/admin/pos" if (REPO / "frontend/src/routes/admin/pos").exists() else None,
                        "os_status": "partial", "visibility": {"roles": ["super_admin", "manager", "cashier (see PosTab)"]},
                        "api_operations": api_calls(f.read_text(), ops_by_norm)})
    return screens


def write_md(ops, screens):
    by_status = Counter(o["os_status"] for o in ops)
    lines = ["# Original API parity matrix", "",
             "Generated by `scripts/parity/build_matrices.py` from the read-only original source and `contracts/openapi.json`.",
             "Recount and accuracy checks: `python3 scripts/parity/recount.py`.", "",
             f"Original operations (one per route decorator and method): **{len(ops)}**", "",
             "## By OS status", "", "| Status | Count |", "|---|---|"]
    lines += [f"| {s} | {by_status.get(s, 0)} |" for s in ("ported", "partial", "missing", "provider-gated", "deprecated-by-design")]
    lines += ["", "## By target phase (missing + partial + provider-gated)", "", "| Phase | Ported | Partial | Missing | Provider-gated | Deprecated |", "|---|---|---|---|---|---|"]
    for phase in (3, 4, 5):
        c = Counter(o["os_status"] for o in ops if o["target_phase"] == phase)
        lines.append(f"| {phase} | {c['ported']} | {c['partial']} | {c['missing']} | {c['provider-gated']} | {c['deprecated-by-design']} |")
    lines += ["", "## By module group", "", "| Module | Phase(s) | Total | Ported | Partial | Missing | Provider-gated | Deprecated |", "|---|---|---|---|---|---|---|---|"]
    groups = defaultdict(list)
    for o in ops:
        groups[o["module_group"]].append(o)
    for g in sorted(groups, key=lambda k: -len(groups[k])):
        c = Counter(o["os_status"] for o in groups[g])
        phases = ",".join(str(p) for p in sorted({o["target_phase"] for o in groups[g]}))
        lines.append(f"| {g} | {phases} | {len(groups[g])} | {c['ported']} | {c['partial']} | {c['missing']} | {c['provider-gated']} | {c['deprecated-by-design']} |")
    ui = Counter((s["surface"], s["os_status"]) for s in screens)
    lines += ["", "## UI matrix summary (`ui-matrix.json`)", "", "| Surface | Partial (OS screen exists) | Missing |", "|---|---|---|"]
    for surface in ("public", "admin", "mobile", "pos"):
        lines.append(f"| {surface} | {ui[(surface, 'partial')]} | {ui[(surface, 'missing')]} |")
    lines += ["", "## Status rules", "",
              "- **ported**: the same method and normalised path exist in `contracts/openapi.json`. Response shape is not asserted identical.",
              "- **partial**: an OS endpoint exists under a new namespace (`/api/admin`, `/api/public`, `/api/mobile`) or with another method.",
              "- **provider-gated**: no OS endpoint, and the handler needs Twilio, Stripe/Payabl, Web Push, OpenAI, social OAuth or email.",
              "- **deprecated-by-design**: seed, debug or test-only endpoints.",
              "- **missing**: everything else. Phase: 5 when mobile auth or `/api/mobile`, 4 when admin auth, otherwise 3.",
              "- Auth, permissions, roles, scope helpers, request fields and side effects are static extracts from each handler body; treat them as evidence pointers to the cited line, not proof of behaviour."]
    (OUT / "api-matrix.md").write_text("\n".join(lines) + "\n")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    ops = build_api()
    screens = build_ui(ops)
    meta = {"generated_by": "scripts/parity/build_matrices.py", "original_source": "MikeCy-com/Lefteria-FC main (uploaded zip, read-only)",
            "os_contract": "contracts/openapi.json"}
    (OUT / "api-matrix.json").write_text(json.dumps({"meta": meta, "operations": ops}, indent=1, ensure_ascii=False) + "\n")
    (OUT / "ui-matrix.json").write_text(json.dumps({"meta": meta, "screens": screens}, indent=1, ensure_ascii=False) + "\n")
    write_md(ops, screens)
    print(f"operations={len(ops)} screens={len(screens)}", dict(Counter(o['os_status'] for o in ops)))


if __name__ == "__main__":
    main()
