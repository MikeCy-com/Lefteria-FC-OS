# Lefteria UI direction

User instruction, 2026-11-01: use the composition and navigation style of
<https://www.juventus.com/en/> as a reference, retaining Lefteria's own gold/black
palette, Fira Sans / Fira Sans Extra Condensed typography, crest and media. Do not
copy Juventus branding, assets or page code. Keep the latest standalone mobile
app visually stable; its existing component/CSS scope stays separate.

The reference was inspected in a real browser: slim icon rail, prominent media
opening, restrained header utilities, strong heading hierarchy and large content
cards. The public adaptation keeps labeled, accessible categories and click-open
submenus, compact branding and editorial news immediately after the hero. The
admin adaptation applies the same typography and hierarchy to actual account,
session and permission data, with clearer navigation and keyboard focus.

The browser's extra preview Basic-auth gate is removed separately. This does
not remove application admin/mobile login, role/organization checks, durable
throttling or mutation guards. Unfinished destinations should return ordinary
application responses instead of repeatedly asking for server credentials.

## Workflow completion

The original GitHub baseline remains380b402. Individual player/detail/statistics
and Academy registration are requested functional work, not styling completion.
Public player projections must preserve source filters and intentional fields,
abbreviate Academy surnames before serialization and exclude private contacts,
DOB, guardian, medical, contracts and account data. Statistics must come from the
recorded source; empty or unavailable data cannot become invented zeroes.

The original Academy registration is an anonymous five-step persisted workflow,
distinct from the customer email/password and mobile OTP identity realms. Its
guardian, emergency, medical, consent and signature fields require a real private
store, validation, abuse controls and a permission-scoped administration workflow.
Do not ship a decorative registration form or claim success without persistence.
