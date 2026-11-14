#!/usr/bin/env python3
"""Read-only mirror of PUBLIC lefteriafc.cy content into /app/.runtime/live-mirror (preview only, never git).
GET only, no login, no cookies, at most 2 requests per second, one attempt per URL (failures are logged
and skipped). Endpoints come from docs/parity/api-matrix.json (public, unauthenticated GET operations)."""
import http.client  # noqa: F401
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

BASE = "https://lefteriafc.cy"
REPO = Path(__file__).resolve().parents[2]
OUT = Path("/app/.runtime/live-mirror")
INTERVAL = 0.5
MAX_IMAGES = int(sys.argv[1]) if len(sys.argv) > 1 else 600
IMAGE = re.compile(r"\.(png|jpe?g|webp|gif|svg|avif)(\?|$)", re.I)
SKIP = re.compile(r"sitemap|robots|/og(/|$)|share|track|analytics|download|export|stream|/(ws|sse)(/|$)|webhook|callback|payment|checkout|unsubscribe", re.I)
last = 0.0
log = None


def get(url, binary=False):
    global last
    if not url.startswith(BASE + "/"):
        return None
    wait = INTERVAL - (time.monotonic() - last)
    if wait > 0:
        time.sleep(wait)
    last = time.monotonic()
    parts = urllib.parse.urlsplit(url)
    url = urllib.parse.urlunsplit(parts._replace(path=urllib.parse.quote(urllib.parse.unquote(parts.path))))
    request = urllib.request.Request(url, method="GET", headers={"User-Agent": "LefteriaOS-preview-mirror/1 (read-only)", "Accept": "*/*"})
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            body = response.read()
            log.write(json.dumps({"ts": round(time.time(), 3), "method": "GET", "url": url, "status": response.status, "bytes": len(body)}) + "\n")
            return body if binary else body.decode("utf-8", "replace")
    except Exception as error:  # one attempt only; logged and skipped
        log.write(json.dumps({"ts": round(time.time(), 3), "method": "GET", "url": url, "status": getattr(error, "code", None), "skipped": str(error)[:120]}) + "\n")
        return None


def save(name, data):
    path = OUT / "api" / (name.strip("/").replace("/", "__") + ".json")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(data)


def walk(value, found):
    if isinstance(value, dict):
        for v in value.values():
            walk(v, found)
    elif isinstance(value, list):
        for v in value:
            walk(v, found)
    elif isinstance(value, str) and (IMAGE.search(value) or "/uploads/" in value) and len(value) < 500:
        found.add(urllib.parse.urljoin(BASE + "/", value))


def items(data):
    if isinstance(data, list):
        return data
    if isinstance(data, dict):
        for key in ("items", "news", "players", "results", "data"):
            if isinstance(data.get(key), list):
                return data[key]
    return []


def main():
    global log
    OUT.mkdir(parents=True, exist_ok=True)
    log = open(OUT / "requests.log", "a", buffering=1)
    ops = json.loads((REPO / "docs/parity/api-matrix.json").read_text())["operations"]
    paths = sorted({o["path"] for o in ops if o["method"] == "GET" and o["auth"] == "public" and o["path"].startswith("/api/")
                    and "{" not in o["path"] and not SKIP.search(o["path"]) and not o["path"].startswith("/api/admin")})
    images, responses = set(), {}
    for path in paths:
        query = "?limit=100" if path in ("/api/news", "/api/players", "/api/fixtures", "/api/gallery") else ""
        body = get(BASE + path + query)
        if body is None and query:
            body = get(BASE + path)
        if body is None:
            continue
        try:
            data = json.loads(body)
        except json.JSONDecodeError:
            continue
        save(path, body)
        responses[path] = data
        walk(data, images)
    for path, detail in (("/api/news", "/api/news/{}"), ("/api/players", "/api/players/{}"), ("/api/staff", "/api/staff/{}"),
                         ("/api/partners", "/api/partners/{}"), ("/api/events", "/api/events/{}")):
        for item in items(responses.get(path)):
            key = item.get("slug") if detail.startswith("/api/partners") else item.get("id")
            if not key:
                continue
            body = get(BASE + detail.format(urllib.parse.quote(str(key))))
            if body:
                save(detail.format(key), body)
                try:
                    walk(json.loads(body), images)
                except json.JSONDecodeError:
                    pass
    media = OUT / "media"
    media.mkdir(exist_ok=True)
    fetched = 0
    for url in sorted(images)[:MAX_IMAGES]:
        name = re.sub(r"[^A-Za-z0-9._-]", "_", urllib.parse.urlparse(url).path.strip("/"))[-150:]
        if (media / name).exists():
            continue
        data = get(url, binary=True)
        if data:
            (media / name).write_bytes(data)
            fetched += 1
    summary = {"api_paths_tried": len(paths), "api_saved": len(list((OUT / "api").glob("*.json"))), "images_found": len(images),
               "images_fetched_this_run": fetched, "news": len(items(responses.get("/api/news"))), "players": len(items(responses.get("/api/players")))}
    (OUT / "summary.json").write_text(json.dumps(summary, indent=1))
    print(json.dumps(summary))


if __name__ == "__main__":
    main()
