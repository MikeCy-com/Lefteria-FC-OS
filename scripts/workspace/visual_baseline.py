#!/usr/bin/env python3
"""Visual baseline: real Chromium screenshots of every public route, live lefteriafc.cy vs the OS preview,
at 390/768/1440/1920 px. Output: /app/.runtime/visual-baseline (never git) + docs/parity/visual-baseline.md.
Live: GET only (any non-GET request is aborted and counted), one navigation at a time, paced.
Run with: PLAYWRIGHT_BROWSERS_PATH=/root/.lefteria/ms-playwright /root/.lefteria/pw-venv/bin/python scripts/workspace/visual_baseline.py"""
import asyncio
import json
import os
import re
import sys
import time
from pathlib import Path

from playwright.async_api import async_playwright

REPO = Path(__file__).resolve().parents[2]
OUT = Path("/app/.runtime/visual-baseline")
LIVE = "https://lefteriafc.cy"
CONF = dict(l.split("=", 1) for l in (REPO / "scripts/workspace/workspace.conf").read_text().split() if "=" in l)
OS = CONF["LEFTERIA_PUBLIC_ORIGIN"]
WIDTHS = [int(w) for w in os.environ.get("VB_WIDTHS", "390,768,1440,1920").split(",")]
PROBE = """() => { const f = s => { const e = document.querySelector(s); if (!e) return null; const c = getComputedStyle(e);
  return {font: c.fontFamily.split(',')[0].replace(/["']/g,''), size: c.fontSize, weight: c.fontWeight}; };
  const main = document.querySelector('main') || document.body; return { title: document.title, h1: f('h1'), body: f('body'),
  nav: f('nav a'), height: document.documentElement.scrollHeight, mainWidth: Math.round(main.getBoundingClientRect().width),
  h1Text: (document.querySelector('h1')||{}).innerText?.slice(0,80) || null, images: document.images.length }; }"""


def routes():
    screens = json.loads((REPO / "docs/parity/ui-matrix.json").read_text())["screens"]
    paths = [s["original"] for s in screens if s["kind"] == "route" and s["surface"] == "public" and ":" not in s["original"] and "*" not in s["original"]]
    mirror = Path("/app/.runtime/live-mirror/api")
    news = sorted(mirror.glob("api__news__*.json"))
    if news:
        paths.append("/news/" + (json.loads(news[0].read_text()).get("slug") or news[0].stem.split("__")[-1]))
    players = sorted(mirror.glob("api__players__*.json"))
    if players:
        paths.append("/player/" + players[0].stem.split("__")[-1])
    manifest = Path("/app/.runtime/live-mirror/manifests/public-sections.json")
    if manifest.exists():
        data = json.loads(manifest.read_text())
        if data.get("academy_groups"):
            paths.append("/academy/" + data["academy_groups"][0]["id"])
        academy = [p for p in data.get("players", []) if p.get("team_type") == "Academy"]
        if academy:
            paths.append("/player/" + academy[0]["id"])
    paths = list(dict.fromkeys(paths))
    only = [p for p in os.environ.get("VB_ROUTES", "").split(",") if p]
    return [p for p in paths if not only or p in only or any(p.startswith(o.rstrip("*")) for o in only if o.endswith("*"))]


async def capture(browser, base, site, path, width, blocked):
    context = await browser.new_context(viewport={"width": width, "height": 900}, device_scale_factor=1)
    page = await context.new_page()

    async def guard(route):
        if route.request.method != "GET" and route.request.url.startswith(LIVE):
            blocked.append(route.request.url)
            await route.abort()
        else:
            await route.continue_()
    await page.route("**/*", guard)
    name = f"{site}_{re.sub(r'[^a-z0-9]+', '_', path.lower()).strip('_') or 'home'}_{width}.jpg"
    result = {"site": site, "path": path, "width": width, "file": name}
    try:
        response = await page.goto(base + path, wait_until="domcontentloaded", timeout=30000)
        await page.wait_for_timeout(2500)
        result["status"] = response.status if response else None
        result.update(await page.evaluate(PROBE))
        await page.screenshot(path=str(OUT / name), type="jpeg", quality=55, full_page=False)
    except Exception as error:
        result["error"] = str(error)[:160]
    await context.close()
    return result


def describe(live, os_):
    if live.get("error") or os_.get("error"):
        return "capture error: " + (live.get("error") or os_.get("error") or "")[:60]
    notes = []
    for key in ("h1", "body", "nav"):
        a, b = live.get(key) or {}, os_.get(key) or {}
        if a and b and (a.get("font") != b.get("font") or a.get("size") != b.get("size")):
            notes.append(f"{key} {a.get('font')} {a.get('size')} vs {b.get('font')} {b.get('size')}")
        elif bool(a) != bool(b):
            notes.append(f"{key} {'missing in OS' if a else 'only in OS'}")
    if live.get("height") and os_.get("height"):
        ratio = os_["height"] / live["height"]
        if abs(ratio - 1) > 0.15:
            notes.append(f"page height {live['height']} vs {os_['height']} px")
    if (live.get("h1Text") or "") != (os_.get("h1Text") or ""):
        notes.append("h1 text differs (see index.json)")
    if live.get("status") != os_.get("status"):
        notes.append(f"HTTP {live.get('status')} vs {os_.get('status')}")
    return "; ".join(notes) or "no measured difference (inspect screenshots)"


async def main():
    OUT.mkdir(parents=True, exist_ok=True)
    blocked, results = [], []
    async with async_playwright() as p:
        browser = await p.chromium.launch()
        for path in routes():
            for width in WIDTHS:
                started = time.monotonic()
                results.append(await capture(browser, LIVE, "live", path, width, blocked))
                results.append(await capture(browser, OS, "os", path, width, blocked))
                await asyncio.sleep(max(0, 3 - (time.monotonic() - started)))
        await browser.close()
    (OUT / ("index.json" if not os.environ.get("VB_ROUTES") else "index.partial.json")).write_text(json.dumps({"live": LIVE, "os": OS, "blocked_non_get_to_live": blocked, "captures": results}, indent=1, ensure_ascii=False))
    pairs = {}
    for r in results:
        pairs.setdefault((r["path"], r["width"]), {})[r["site"]] = r
    md = ["# Visual baseline: live lefteriafc.cy vs OS preview", "",
          "Generated by `scripts/workspace/visual_baseline.py`. Screenshots and the raw index live in `/app/.runtime/visual-baseline/` "
          "(git-ignored). Measured with real Chromium: computed font family and size of `h1`, `body` and the first `nav a`, "
          "page height and `h1` text.", "", f"Non-GET requests to live that were blocked: {len(blocked)}", "",
          "| Route | Width | Live | OS | Differences |", "|---|---|---|---|---|"]
    for (path, width), pair in pairs.items():
        live, os_ = pair.get("live", {}), pair.get("os", {})
        label = re.sub(r"^/(news|player|academy)/(?!groups|philosophy|registration).+$", lambda m: f"/{m.group(1)}/:id (sample from mirror)", path)
        md.append(f"| `{label}` | {width} | {live.get('status')} | {os_.get('status')} | {describe(live, os_).replace('|', '/')} |")
    (REPO / "docs/parity/visual-baseline.md" if not os.environ.get("VB_ROUTES") else OUT / "partial.md").write_text("\n".join(md) + "\n")
    print(f"captures={len(results)} blocked_non_get={len(blocked)}")


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
