# Public layout restoration release

Baseline: `MikeCy-com/Lefteria-FC@380b4022f0ab0db95fbc52a8613711160a5037a5`,
including the public shell, original CSS, homepage, navigation and legal pages.
This is a bounded correction to the separate preview, not full feature parity.

## Included

- Restore the original typography, spacing, sidebar categories, click-open
  submenus, shared FC branding, header actions, homepage sections and footer.
- Native modal navigation/search with focus handling, Escape, scroll restoration
  and reduced-motion support. Search reads the existing reviewed public news DTO.
- Restore About, Academy philosophy, privacy and terms content from the baseline.
- The next-match countdown uses a server snapshot and unique Cyprus kickoff
  instants. Missing times stay date-only; invalid/DST gap/fold times are not
  assigned guessed offsets. Imported fixture bytes and database schema do not
  change. Regenerate OpenAPI and the TypeScript client together.
- Explicitly open only the additional static public routes at the preview edge;
  administrator/member pages and private API boundaries keep their protections.

## Remaining work

Restoring a menu entry does not complete its destination. Roster/detail, gallery,
venues, standings, Academy group/detail, registration, trials, membership,
sponsors/partners, archived seasons, voting, events, shop/customer/cart and payment
workflows still require their mapped data and actions. Several current links
therefore lead to unfinished routes. Public notifications are visibly disabled.
Private restored media is not published wholesale. The preserved club editor
and broader admin refactor are separate pending integrations.

Do not describe this release as completion of the public website or administration
migration. Keep production authoritative and do not attach `lefteriafc.cy` to the
preview. Twilio setup remains paused.
