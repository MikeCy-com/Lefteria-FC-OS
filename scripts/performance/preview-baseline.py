#!/usr/bin/env python3
"""Low-traffic, read-only baseline for the isolated Lefteria OS preview.

No authentication, writes, redirects, proxy use, production targets or load tests.
Run only after the operator has checked the deployed revision and isolation.
"""
import argparse
import datetime
import json
import math
import re
import ssl
import time
import urllib.error
import urllib.request

PREVIEW = "https://srv1649602.hstgr.cloud:8443"
MAX_BODY = 8 * 1024 * 1024
ROUTES = (
    ("/", "html", None),
    ("/news", "html", None),
    ("/academy", "html", None),
    ("/team", "html", None),
    ("/robots.txt", "robots", None),
    ("/api/public/site?organization_id=lefteria_fc", "site", "lefteria_fc"),
    ("/api/public/site?organization_id=lefteria_fc_academy", "site", "lefteria_fc_academy"),
    ("/api/public/news?offset=0&limit=12", "news", None),
    ("/api/public/sections?organization_id=lefteria_fc", "site", "lefteria_fc"),
    ("/api/public/website/pages?organization_id=lefteria_fc", "pages", None),
)

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None

def validate_target(target, revision):
    if target != PREVIEW:
        raise ValueError("Only the reviewed HTTPS preview origin is permitted")
    if not re.fullmatch(r"[a-f0-9]{40}", revision):
        raise ValueError("Provide the operator-verified deployed commit SHA")

def p95(values):
    return round(sorted(values)[math.ceil(len(values) * .95) - 1], 2) if values else None

def validate_payload(kind, owner, headers, body):
    media = headers.get("Content-Type", "").split(";")[0].strip().lower()
    robots = headers.get("X-Robots-Tag", "").lower()
    cache = headers.get("Cache-Control", "").lower()
    if "noindex" not in robots or "no-store" not in cache:
        return False
    if kind == "html":
        return media == "text/html" and b"<html" in body.lower()
    if kind == "robots":
        return media == "text/plain" and "Disallow: /" in body.decode("utf-8").splitlines()
    if media != "application/json":
        return False
    data = json.loads(body)
    if kind == "pages":
        return isinstance(data, list)
    if not isinstance(data, dict):
        return False
    if kind == "site":
        return data.get("organization_id") == owner
    if kind == "news":
        total, items = data.get("total"), data.get("items")
        return (type(total) is int and total >= 0 and isinstance(items, list)
                and len(items) == min(total, 12))
    return False

def probe(opener, target, route):
    path, kind, owner = route
    start = time.monotonic()
    status, size, valid, failure = None, 0, False, None
    try:
        req = urllib.request.Request(target + path, headers={
            "User-Agent": "Lefteria-Preview-Baseline/1.0", "Accept-Encoding": "identity"
        })
        with opener.open(req, timeout=5) as response:
            status = response.status
            body = response.read(MAX_BODY + 1)
            size = len(body)
            valid = status == 200 and size <= MAX_BODY and validate_payload(
                kind, owner, response.headers, body)
            if not valid:
                failure = "unexpected_status_headers_or_payload"
    except urllib.error.HTTPError as error:
        status = error.code
        error.close()
        failure = "http_error_or_redirect"
    except (OSError, ValueError, UnicodeError):
        failure = "transport_or_invalid_payload"
    return {"status": status, "bytes": size, "duration_ms": round(
        (time.monotonic() - start) * 1000, 2), "valid": valid, "failure": failure}

def run(target, revision):
    validate_target(target, revision)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(),
                                         urllib.request.HTTPSHandler(context=ssl.create_default_context()))
    rows = {route[0]: [] for route in ROUTES}
    started = datetime.datetime.now(datetime.timezone.utc).isoformat()
    # Hard-coded 3 sequential rounds (30 requests maximum). Stop on the first
    # failed route, especially an overload response or unexpected preview gate.
    aborted = False
    for _ in range(3):
        for route in ROUTES:
            result = probe(opener, target, route)
            rows[route[0]].append(result)
            if not result["valid"]:
                aborted = True
                break
            time.sleep(1)
        if aborted:
            break
    report = {
        "target": target, "operator_reported_revision": revision,
        "started_utc": started, "finished_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "kind": "sequential_public_baseline_not_capacity_test",
        "aborted": aborted,
        "routes": {path: {"samples": samples,
                          "successful_p95_ms": p95([s["duration_ms"] for s in samples if s["valid"]])}
                   for path, samples in rows.items()},
    }
    print(json.dumps(report, indent=2))
    return 1 if aborted else 0

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", required=True)
    parser.add_argument("--deployed-sha", required=True)
    parser.add_argument("--confirm-isolated-preview", action="store_true", required=True)
    args = parser.parse_args()
    try:
        validate_target(args.target, args.deployed_sha)
    except ValueError as error:
        parser.error(str(error))
    return run(args.target, args.deployed_sha)

if __name__ == "__main__":
    raise SystemExit(main())
