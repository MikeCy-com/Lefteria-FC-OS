#!/usr/bin/env python3
"""Builds public-content and public-sections import manifests from the read-only live mirror
(/app/.runtime/live-mirror/api) into /app/.runtime/live-mirror/manifests. Preview only; never git."""
import json
from datetime import datetime, timezone
from pathlib import Path

MIRROR = Path("/app/.runtime/live-mirror")
API = MIRROR / "api"
OUT = MIRROR / "manifests"
COMMIT = "380b4022f0ab0db95fbc52a8613711160a5037a5"  # source-contract baseline used by the public import docs
SECTIONS = ["players", "staff", "standings", "venues", "seasons", "gallery", "sponsors", "partners", "marketing_events", "products",
            "trials_settings", "fixtures", "teams", "academy_groups", "tickets", "archived_seasons", "reviews", "training_sessions",
            "attendance", "potm_votes", "club_profile"]
SOURCE = {"players": "players", "staff": "staff", "standings": "standings", "venues": "venues", "seasons": "seasons",
          "gallery": "gallery", "sponsors": "sponsors", "partners": "partners", "marketing_events": "events", "products": "products",
          "fixtures": "fixtures", "teams": "teams", "academy_groups": "academy-groups", "tickets": "tickets",
          "archived_seasons": "seasons__archive", "reviews": "reviews"}


def load(name):
    path = API / f"api__{name}.json"
    if not path.exists():
        return []
    data = json.loads(path.read_text())
    if isinstance(data, dict):
        for key in ("items", "news", "players", "results", "data", "events", "partners"):
            if isinstance(data.get(key), list):
                return data[key]
        return [data]
    return data


def detailed(name, rows):
    out = []
    for row in rows:
        detail = API / f"api__{name}__{row.get('id')}.json"
        out.append(json.loads(detail.read_text()) if detail.exists() else row)
    return out


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    club = load("club")
    rows = {name: load(source) for name, source in SOURCE.items()}
    rows["players"] = detailed("players", rows["players"])
    rows["club_profile"] = club
    for name in SECTIONS:
        rows.setdefault(name, [])
    news = detailed("news", load("news"))
    header = {"schema_version": 1, "source_repository": "MikeCy-com/Lefteria-FC", "source_commit": COMMIT}
    public = {**header, "complete_collections": ["club_profile", "news", "teams", "academy_groups", "fixtures"],
              "club_profile": club, "news": news, "teams": rows["teams"], "academy_groups": rows["academy_groups"], "fixtures": rows["fixtures"]}
    (OUT / "public-content.json").write_text(json.dumps(public, ensure_ascii=False))
    sections = {"schema_version": 1, "snapshot_version": 1, **{k: v for k, v in header.items() if k != "schema_version"},
                "base_public_sha256": "0" * 64, "exported_at": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
                "complete_collections": SECTIONS, **{name: rows[name] for name in SECTIONS}}
    (OUT / "public-sections.json").write_text(json.dumps(sections, ensure_ascii=False))
    print(json.dumps({"news": len(news), **{k: len(v) for k, v in rows.items() if v}}))


if __name__ == "__main__":
    main()
