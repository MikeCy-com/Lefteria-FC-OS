# GitHub Actions runner-start failure — recovery and non-deploy triage

**Observed:** 2026-11-09 on `MikeCy-com/Lefteria-FC-OS`.
**Applies to:** OS `main`, and developer PRs #142, #143, #145 and #147.
**No production deployment permitted as part of the investigation.**

## Verified evidence

- The OS `main` Rewrite build run
  [42592840151](https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/42592840151)
  failed on all five jobs before runner assignment. GitHub's job response
  shows `runner_id=0`, zero executed steps, and completion in seconds.
- Two separate rerun requests for this run were accepted by GitHub, but
  new attempts again failed at the pre-runner stage; attempting to fetch
  a job log returned a missing log blob. There is no compiler, test, or
  container error log for these attempts.
- PR #142's last earlier *runnable* run
  [42518985513](https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/42518985513)
  completed 3/5 jobs. Its actual PostgreSQL failure was
  `42703: column "secs" does not exist`; the migration used
  `make_interval(secs=...)` rather than PostgreSQL named notation
  `make_interval(secs=>...)`. The Web Push branch now has this corrected,
  as well as updated OpenAPI and TypeScript generation outputs that had
  failed the contract diff check. **Neither fix has passed a new runnable CI.**
- PR #143's newest run
  [42595085069](https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/42595085069),
  PR #145's
  [42601278984](https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/42601278984),
  and PR #147's
  [42601750837](https://github.com/MikeCy-com/Lefteria-FC-OS/actions/runs/42601750837)
  likewise failed before runner execution.
- GitHub Status published a 2026-11-09 incident regarding high 504 error
  rates for *anonymous requests and release downloads*, but this is **not
  established as the cause** of jobs failing before a runner starts.

**Current diagnosis:** GitHub Actions execution/runner admission is
unavailable for this repository. Its exact reason is unknown because
the available connector can inspect runs and request reruns, but cannot
read account billing or repository administrative Actions settings.

## Required owner/admin checks — do these in GitHub UI

1. Open **Lefteria-FC-OS → Actions → Rewrite build → failed run**.
   Inspect any top-level error banner or job annotation before the runner
   begins. A banner about account spending, usage, payment, runners or
   repository policy is more useful than empty job logs.
2. In the repository **Settings → Actions → General**, verify that
   the workflow and GitHub-hosted Ubuntu runners are allowed. Review
   organization/enterprise policy restrictions where applicable.
3. In the account/organization **Settings → Billing / Usage**, check
   GitHub Actions usage, budget limits and any payment failures. Only the
   owner can correct a billing or plan hold; do **not** paste billing or
   card information into repository files, logs or chat.
4. Review GitHub Actions status at <https://www.githubstatus.com/> and
   ask GitHub Support to investigate if the runner-start failure persists
   with no account-side banner.
5. Once runner admission is restored, rerun failed jobs or trigger a
   new PR run. Do not treat the existing failure as proof that application
   code passed.

## Non-deploying merge gate

- Confirm 5 green jobs on the **exact PR head commit** for ASP.NET Core API,
  SvelteKit web, Docker Compose build, Mobile identity with PostgreSQL,
  and API contract and startup smoke.
- For PR #142, confirm the original `secs` integration check and generated
  API contract both pass on its latest head.
- For PR #143, verify EF migration IDs are distinct and sequential, the
  complete Payabl/Academy billing lifecycle, cash/card/PDF tests and updated
  OpenAPI and TS contract generation. Merchant sandbox approval must happen
  before any money movement and is separate from green CI.
- For PR #145, verify Academy-only CRM grants, transactional read/update/audit
  tests and denied cross-organization access.
- For PR #147, verify the attendance migration and disposable lifecycle SQL
  including inherited RSVP vs actual presence, status replay, version
  conflicts and revoked session access.
- **Never deploy** to Hostinger or switch traffic while testing. Do not
  modify `MikeCy-com/Lefteria-FC` main. A successful merge to OS main is
  not proof of production readiness.

The source-to-OS launch gap register lives at
`docs/implementation/go-live-gap-audit-2026-11-09.md`.
