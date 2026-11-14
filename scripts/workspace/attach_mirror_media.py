#!/usr/bin/env python3
"""Preview only: copies mirrored public images to the API's public media root under their original
/api/uploads/<folder>/<file> path, so imported rows resolve. Never git."""
import json
import re
import shutil
import sys
import urllib.parse
from pathlib import Path

MIRROR = Path("/app/.runtime/live-mirror")
ROOT = Path(sys.argv[1] if len(sys.argv) > 1 else "/app/.runtime/public-media")
PUBLIC = {"players", "staff", "news", "partners", "sponsors", "gallery", "opponent", "groups", "academy",
          "products", "venues", "club", "teams", "events", "reels", "standings"}


def urls(value, found):
    if isinstance(value, dict):
        for item in value.values():
            urls(item, found)
    elif isinstance(value, list):
        for item in value:
            urls(item, found)
    elif isinstance(value, str) and "/api/uploads/" in value:
        found.add(urllib.parse.urlparse(value).path)


def main():
    found = set()
    for manifest in (MIRROR / "manifests").glob("*.json"):
        urls(json.loads(manifest.read_text()), found)
    attached = missing = 0
    for path in sorted(found):
        parts = path.split("/api/uploads/", 1)[1].split("/")
        if len(parts) != 2 or parts[0] not in PUBLIC:
            continue
        name = re.sub(r"[^A-Za-z0-9._-]", "_", path.strip("/"))[-150:]
        source = MIRROR / "media" / name
        if not source.exists():
            missing += 1
            continue
        target = ROOT / parts[0] / parts[1]
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        attached += 1
    print(json.dumps({"media_attached": attached, "media_missing": missing, "root": str(ROOT)}))


if __name__ == "__main__":
    main()
