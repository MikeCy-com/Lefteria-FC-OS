# Visual parity: original screenshot-by-screenshot gate (15 November 2026)

## What is confirmed, and what is not

The main OS browser audit captures page states at 390, 768, 1440 and 3840 CSS pixels. It also captures synthetic authorized-admin sidebar states and isolated five-role components. **None of those captures by itself is an original-vs-OS pixel comparison.** The original snapshot directory from the source-project visual audit is not in this Git checkout: `/app/.runtime/visual-baseline/` was git-ignored. Older chat image attachments are not present in the current conversation's accessible files.

`frontend/scripts/compare-reference-screenshots.mjs` now performs an exact PNG per-pixel comparison if and only if the original-reference screenshot files are actually available. Without originals, it writes `UNVERIFIED_REFERENCE_IMAGES_MISSING` into the CI screenshot artifact and emits a warning, never a false pass. In strict mode it fails if even one expected reference image is missing. The same source files and test data must be used for both screenshots; compare public routes with published Academy and Club fixtures, not a 503 "unavailable" page against populated production content.

## Review procedure

1. Obtain approved original images from the user's supplied UI references or the documented previous Chromium source snapshot. Confirm they correspond to the **same route, viewport dimensions, device scale factor, content, language, consent state, font loading and capture position** as the new screenshots.
2. Place the approved PNG files outside Git in a private, read-only reference directory, using the identical filenames from `results.json` and `role-results.json`. The OS screenshots and JSON manifests live in `LEFTERIA_UI_ARTIFACTS`. Do not publish screenshots containing parent/player identities or admin screens to a public artifact.
3. Execute from `frontend/`:

   ```bash
   LEFTERIA_UI_ARTIFACTS=/secure/os-captures \
   LEFTERIA_REFERENCE_SCREENSHOT_DIR=/secure/original-approved-pngs \
   LEFTERIA_REQUIRE_PIXEL_PARITY=1 \
   LEFTERIA_MAX_PIXEL_CHANGED_RATIO=0.02 \
   LEFTERIA_PIXEL_COLOR_TOLERANCE=12 \
   node scripts/compare-reference-screenshots.mjs
   ```

4. Inspect `visual-pixel-comparison.json`: each capture records original/OS pixel dimensions, changed-pixel ratio, mean normalized RGB error, first mismatching coordinate, review threshold and status. The gate rejects size differences and more than 2% changed pixels by default. **This is a geometric/pixel signal, not an accessibility or functional endorsement.**
5. Fix mismatches route-by-route, recapture with unchanged settings and rerun until the required complete set passes. Independently check keyboard accessibility, Academy-vs-First-Team scoping, backend CMS publishing and authorization.

The PNG comparator is self-tested in GitHub CI with an exact 1-of-24 pixel change. CI separately reports **unverified originals** unless an approved same-viewport reference directory has been mounted; this is intentional. Original documents/screenshots must not be fabricated from the new OS code just to make the test pass.

### Unfinished acceptance criteria

- The user's exact screenshot references are still not available to the current execution environment; **full pixel parity is unverified**.
- Authenticated admin screenshots use isolated authorization fixtures, not genuine staging credential/session E2E.
- Public roster and Academy group pages require populated staging projections to validate the real cards/portraits rather than unavailable/error states.
- Full role-by-role functional journeys, API behavior and interactive visual parity still require actual isolated staging verification. No live Hostinger production deployment is authorized.
