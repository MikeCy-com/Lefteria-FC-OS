#!/usr/bin/env python3
"""Builds docs/parity/phase-plan.md from api-matrix.json + ui-matrix.json. Every missing/partial/provider-gated
operation and every non-ported screen is assigned to exactly one buildable sub-phase (verified at the end)."""
import collections
import json
import sys
from pathlib import Path

DOCS = Path(__file__).resolve().parents[2] / "docs/parity"
PLAN = {
    3: [("3.1", "Public design system and shell", "Tokens, fonts, page widths, header/footer/nav, breakpoints from design-tokens.md; every public screen", []),
        ("3.2", "Club, squad and season data", "Club profile, teams, players, staff, seasons, standings, venues, academy, transfers",
         ["club", "teams", "players", "staff", "seasons", "standings", "venues", "current-season", "transfers", "academy", "academy-groups", "academy_matches", "drills"]),
        ("3.3", "News, media, fixtures and SEO", "News/posts, gallery, videos, reels, fixtures, calendar, live match, OG/SEO, changelog",
         ["news", "posts", "gallery", "videos", "reels", "fixtures", "calendar", "live-match", "live-status", "og", "seo", "changelog", "root", "health", "analytics", "app_download", "settings"]),
        ("3.4", "Public engagement and forms", "Votes, predictions, reviews, contact, registrations, trials, referrals, sponsors/partners, events, share gate",
         ["votes", "predictions", "reviews", "contact", "registrations", "trials", "referrals", "share_gate", "sponsor_interactions", "sponsors", "partners", "events", "marketing_events", "marketing", "social_publishing", "push", "auth"]),
        ("3.5", "Shop, tickets and checkout", "Products, cart, customer accounts, orders, tickets, loyalty, POS public, Stripe checkout (provider OFF)",
         ["products", "cart", "customer", "orders", "tickets", "loyalty", "pos", "stripe_checkout"])],
    4: [("4.1", "Admin shell and dynamic permission contract", "Admin layout/nav, users, roles, security, backup, notifications, calendar, messages",
         ["admin", "auth", "admin_users", "security_ops", "backup", "seed", "health", "changelog", "notification_center", "admin_calendar", "admin_messages"]),
        ("4.2", "Website content management", "Media library, videos, reels, partners, events, staff, polls, suggestions, reviews, resources",
         ["media_library", "communication_media", "videos", "reels", "partners", "marketing_events", "staff", "polls", "suggestions", "reviews", "resources", "app_download"]),
        ("4.3", "Football operations", "Drills, opponents, rosters, match submissions, player documents/evaluations/CRM, trials, staff compliance, inventory",
         ["drills", "opponents", "match_rosters", "match_submissions", "player_documents", "player_evaluations", "players_crm", "trials", "staff_compliance", "storage_inventory"]),
        ("4.4", "Finance and commerce", "Charges, expenses, financial reports, quarterly report, POS, loyalty, referrals",
         ["charges", "expenses", "financial", "quarterly_report", "pos", "loyalty", "referrals"]),
        ("4.5", "Marketing and social control", "Marketing, social calendar/communities/control/publishing, fan engagement, analytics, chat moderation",
         ["marketing", "social_calendar", "social_communities", "social_control", "social_publishing", "fan_engagement", "analytics", "chat_moderation"])],
    5: [("5.1", "Mobile auth, account and family", "OTP auth, profile, family links, uploads, role dashboards", ["mobile_auth", "family", "uploads"]),
        ("5.2", "Notifications and push", "Notification centre and web push (provider OFF)", ["notification_center", "push"]),
        ("5.3", "Fan and social network", "Feed, communities, chat moderation, polls, MVP, reels, suggestions, media",
         ["social_network", "social_communities", "social_control", "social_media", "fan_engagement", "polls", "mvp", "reels", "suggestions", "chat_moderation", "communication_media"]),
        ("5.4", "Payments and member development", "Charges, Stripe checkout (provider OFF), sponsor interactions, member development, match submissions",
         ["charges", "stripe_checkout", "sponsor_interactions", "member_development", "match_submissions"])],
}
SURFACE_PHASE = {"public": "3.1", "admin": "4.1", "pos": "4.4", "mobile": "5.1"}


def main():
    ops = json.loads((DOCS / "api-matrix.json").read_text())["operations"]
    screens = json.loads((DOCS / "ui-matrix.json").read_text())["screens"]
    open_ops = [o for o in ops if o["os_status"] in ("missing", "partial", "provider-gated")]
    assigned, lines = {}, []
    for phase, subs in PLAN.items():
        for code, _, _, modules in subs:
            for o in open_ops:
                if o["target_phase"] == phase and o["module_group"] in modules and o["id"] not in assigned:
                    assigned[o["id"]] = code
    unassigned = [o for o in open_ops if o["id"] not in assigned]
    screen_sub = {s["id"]: SURFACE_PHASE[s["surface"]] for s in screens if s["os_status"] != "ported"}
    lines += ["# Phase plan: public (3) → admin (4) → mobile (5)", "",
              "Generated by `scripts/parity/phase_plan.py` from `api-matrix.json` and `ui-matrix.json`. Every operation with status "
              "missing, partial or provider-gated and every non-ported screen is assigned to exactly one sub-phase below.", "",
              f"Open operations: {len(open_ops)} · assigned: {len(assigned)} · unassigned: {len(unassigned)} · "
              f"non-ported screens: {len(screen_sub)}", "",
              "## Rules that apply to every sub-phase", "",
              "- Native stack only (SvelteKit + TypeScript, ASP.NET Core .NET 10, PostgreSQL 18); contract-first: `contracts/openapi.json` changes with each endpoint.",
              "- Dynamic permission contract: the UI renders from the backend's session payload (roles, capabilities, organization grants, module flags); "
              "nav items, tabs, buttons and fields appear/disappear when the backend changes them, without a redeploy. The server re-checks every permission "
              "on every request; the UI never is the gate. Original visibility rules per screen are in `ui-matrix.json` (`visibility`).",
              "- Providers are built but OFF until keys are added (Twilio SMS, Payabl/Stripe checkout, WebPush/VAPID, social publishing, AI). "
              "Disabled providers return the existing safe unavailable state.",
              "- Each sub-phase ends with: matrix rows flipped to `ported` only with contract + test evidence, visual-baseline rerun for touched routes, "
              "policy checks, unit/integration tests.", ""]
    for phase, subs in PLAN.items():
        lines += [f"## Phase {phase}", ""]
        for code, title, scope, _ in subs:
            mine = [o for o in open_ops if assigned.get(o["id"]) == code]
            mine_screens = [s for s in screens if screen_sub.get(s["id"]) == code]
            counts = collections.Counter(o["os_status"] for o in mine)
            lines += [f"### {code} {title}", "", scope + ".", "",
                      f"Operations: {len(mine)} ({', '.join(f'{k} {v}' for k, v in sorted(counts.items())) or 'none'}) · screens: {len(mine_screens)}", ""]
            providers = [o for o in mine if o["os_status"] == "provider-gated"]
            if providers:
                lines += ["Providers needed (OFF until keys): " + "; ".join(sorted({o['status_reason'][:80] for o in providers})), ""]
            if mine:
                lines += ["<details><summary>Operations</summary>", ""] + \
                         [f"- `{o['method']} {o['path']}` — {o['os_status']} ({o['module_group']})" for o in mine] + ["", "</details>", ""]
            if mine_screens:
                lines += ["<details><summary>Screens</summary>", ""] + \
                         [f"- {s['kind']} `{s['original']}` — {s['os_status']}" for s in mine_screens] + ["", "</details>", ""]
    if unassigned:
        lines += ["## Unassigned (must be empty)", ""] + [f"- {o['id']} {o['method']} {o['path']}" for o in unassigned]
    (DOCS / "phase-plan.md").write_text("\n".join(lines) + "\n")
    print(f"open={len(open_ops)} assigned={len(assigned)} unassigned={len(unassigned)} screens={len(screen_sub)}")
    return 1 if unassigned else 0


if __name__ == "__main__":
    sys.exit(main())
