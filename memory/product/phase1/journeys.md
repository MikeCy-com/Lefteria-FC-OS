# Phase 1 journeys: Run Lefteria FC OS in this workspace

Demo phones are printed by the seeding tool and recorded in `/app/memory/test_credentials.md`. The code is always `123456`. The calendar fixture month is October 2026.

## Platform

- **JRN-1**: open `<preview>/api/health/live`, then `<preview>/api/health/ready`.
  - expected: both return HTTP 200.
- **JRN-2**: open `<preview>/api/openapi.json`.
  - expected: a JSON OpenAPI document with the same path set as `contracts/openapi.json` (172 paths), including `/api/mobile/auth/request-otp` and `/api/admin/auth/setup`.
- **JRN-3**: restart the backend and frontend services, wait for readiness, then sign in again as the parent demo phone and open the October 2026 calendar.
  - expected: no manual step needed. The same demo phones still work, and the same October events appear as before the restart.
- **JRN-4**: request an `/api/*` URL while the API host is stopped.
  - expected: HTTP 503, not a hang or an HTML page.

## Public website

- **JRN-5**: open the preview root `/`.
  - expected: the Greek club homepage renders with the club logo and navigation. No server error page is shown.
- **JRN-6**: open each of `/news`, `/team`, `/academy`, `/fixtures`, `/contact`, `/shop`, `/trials`.
  - expected: each page renders either content or the repo's own explicit empty or unavailable state. None returns HTTP 500 or a blank page.

## Admin owner setup (one-time, belongs to the user)

- **JRN-7**: open `/admin/login`.
  - expected: a sign-in form plus the owner setup option are shown.
- **JRN-8**: choose owner setup and enter a wrong 64-character hex code plus a valid password.
  - expected: setup is rejected with a safe error. No account is created, and the setup option is still offered afterwards.
- **JRN-9**: sign in at `/admin/login` with a made-up username and password.
  - expected: a safe sign-in error. The user stays on the login page.
- **JRN-10** (human only, the user): choose owner setup, enter the one-time setup code and a password of at least 12 characters, then sign in.
  - expected: the admin workspace opens for both Club and Academy. The setup option is gone afterwards.

## Mobile app test logins

- **JRN-11**: open `/app/login`, enter the player/fan demo phone, request a code, enter `123456`.
  - expected: signed in. A role picker or role home appears with the player and fan roles available, and the header shows the role changer, notifications and profile.
- **JRN-12**: on `/app/login`, enter a demo phone, request a code, enter `000000`.
  - expected: the app's safe error is shown and the user stays signed out.
- **JRN-13**: on `/app/login`, enter a phone that is not a demo number (for example `+35799000000`) and request a code.
  - expected: the app's existing safe failure. No code is accepted afterwards. No SMS provider is contacted.
- **JRN-14**: sign in as the parent demo phone and open the calendar for October 2026.
  - expected: three linked children are selectable (one FC child, two in different Academy groups). Choosing the second child shows that child's events only.
- **JRN-15**: as the parent, open an October 2026 event that allows replies, set RSVP to "maybe", then refresh the page.
  - expected: the RSVP still shows "maybe" after the refresh.
- **JRN-16**: as the parent, look at October 21 2026.
  - expected: the staff-only draft event is not visible.
- **JRN-17**: sign in as the coach/management demo phone, switch to management, then switch between FC and Academy.
  - expected: the FC/Academy selector works. Academy events (October 20) show only in Academy and FC events (October 12) only in FC.
- **JRN-18**: as coach or management, open an October event with replies.
  - expected: RSVP replies from parents/players are listed. No control to RSVP as staff succeeds.
- **JRN-19**: as the player/fan demo user, open the profile, change the first name, save, then refresh.
  - expected: the new name is still shown after the refresh.
- **JRN-20**: as any demo user, open a function the repo has not ported yet.
  - expected: it shows the exact label **Under Developments**.

## Disabled services

- **JRN-21**: as the parent demo user, open the training-fee or payments area.
  - expected: the app's existing unavailable or disabled state. No external payment page opens.
- **JRN-22**: as any demo user, open notification settings and try to enable device push.
  - expected: push is shown as unavailable, or the app's existing disabled state. No error page.
