# Player birthday card preferences — private administration slice

This slice ports the original Lefteria FC birthday card configuration
concept into OS admin **without** automatically publishing a child's
name, birthday, photograph or celebration to the public or social networks.

## Implemented

- Per-player, organization-scoped settings stored separately from the
  imported roster: disabled, Greek wish text (500 characters), one of
  eight branded colors, and an approved emoji.
- Privacy-first defaults: disabled=true; public_enabled=false;
  auto_post_facebook=false; auto_post_instagram=false. These last three
  fields are immutable response flags, **not** user-changeable toggles.
- Manager-only authenticated GET/PUT with a current owner grant checked
  again in PostgreSQL, active-player validation, exact optimistic version,
  immutable change snapshots and security audit events.
- Admin private-player-record panel with safe defaults, explicit opt-out
  and no publishing or social-post actions.
- RLS on settings and event history; no direct admin/mobile table access.
  Separate operator-controlled
  `ops/postgres/player-birthday-admin-grants.sql` grants only two
  reviewed functions to the web admin auth runtime. Never run against
  original Lefteria FC or Hostinger production.

## Still missing

This is **not** an automated birthday feature yet. Birthday scheduling,
DOB-based eligibility, parental/public consent, image upload and scanning,
birthday card rendering, age privacy, admin review of a proposed public
post, notification opt-ins and Facebook/Instagram provider integrations
must be implemented and independently verified before publication can
be enabled. Until then even disabled=false remains **private**.

## Staging verification

Apply migrations in isolated PostgreSQL, review explicit role grants,
test manager reads and version conflicts, verify cross-owner and revoked
sessions deny, inspect audit rows, and confirm no birthday post appears
on public website, social feeds or parent/player app. Do not deploy live.


## Private responsive branded preview

Managers viewing an already-authorized player record can toggle a
responsive 16:9 visual preview using that player's roster display name,
the currently selected birthday greeting, color and emoji. The original
Lefteria FC birthday settings had a preview; this brings the admin
workflow closer to that experience without using a public endpoint.

The preview shows an **unmistakable PRIVATE / NOT FOR PUBLICATION
watermark** and a full disabled overlay whenever the card is opted out.
Unsaved edits appear only as a temporary local preview; this is not
a publication, downloadable export, social post, notification or
parent-approved greeting. Parental consent, scheduling and vetted
media uploads are still not implemented.
