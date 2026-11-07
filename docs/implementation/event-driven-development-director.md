# Lefteria FC OS event-driven development director

This integration is **not a ChatGPT chat timer**. It runs inside the **Lefteria-FC-OS GitHub repository**, using GitHub Actions to assign one queued issue to the GitHub Copilot cloud coding agent at a time. GitHub Copilot plan access and a user-authorized token are required. It never reads/writes production infrastructure.

## Components

- `.github/agents/lefteria-director.agent.md`: owner-aligned priorities and quality director, used for preparing actionable issue handoffs.
- `.github/agents/lefteria-developer.agent.md`: isolated implementation and testing instructions for the coding agent.
- `.github/workflows/development-director.yml`: **event-triggered** dispatcher (no hourly schedule).
- `ops/agents/dispatch-next.sh`: single-flight issue selector and authenticated Copilot assignment.

## Activation required

1. Confirm GitHub Copilot **cloud coding agent** is available to the repository owner and enabled for `MikeCy-com/Lefteria-FC-OS`.
2. Create a **user-to-server** GitHub token authorized for Copilot assignment and OS repository Issues write access (do not paste it into a chat, source file or logs).
3. Set the token as the **Actions repository secret** `LEFTERIA_COPILOT_ASSIGN_TOKEN` on the **OS repository only**. The ordinary Actions `GITHUB_TOKEN` installation token is not sufficient to start Copilot cloud agent tasks.
4. Confirm the GitHub Actions workflows and custom Copilot agent profile are enabled on the default branch. **Do not enable Copilot's production-hosting or deployment permissions.**
5. Open a clearly scoped OS issue, label it `agent-ready`, and supply exact acceptance and security criteria. You may stage several prioritized issues; the oldest one is selected first. You can also invoke the dispatcher via workflow_dispatch.

Without the secret, the dispatcher reports a warning and **does not claim that a coding agent started**.

## Event contract

The dispatcher wakes when:
- an OS issue receives the `agent-ready` label;
- the OS `Rewrite build` workflow completes **successfully** for a `push` on `main` from this repository; or
- an authorized user manually dispatches the workflow.

It does **not** have an hourly timer. The exact instant a developer stops working without posting a GitHub event is **not observable** by this workflow. When one open issue bears `agent-running`, it will not dispatch a second. The coding agent must open a PR that closes its task (`Fixes #...`); after the verified PR merges and the issue closes, a successful main-build event can dispatch the next queued task. A failing or abandoned issue remains blocking until its fix or explicit recovery action, preventing uncontrolled parallel work. The director cannot automatically invent a new issue when the queue empties; someone or an authorized director process must queue new tasks. This preserves safety.

## Mandatory controls

- **Never write to** `MikeCy-com/Lefteria-FC` or touch live `lefteriafc.cy`, Hostinger production, Docker, database, DNS or credentials.
- No SSH connection, database credentials, deploy token or Hostinger secret in this workflow. Even the isolated `lefteria-os-preview` remains a separate, explicitly verified release gate.
- All code goes to `MikeCy-com/Lefteria-FC-OS` feature branches and PRs; no direct auto-merge. Inspect the current PR head, permissions, five CI job conclusions and security review before merging.
- Tasks using parent/child medical, consent, payment, identity, role switching or OTP must retain privacy and authorization gates, and not collect live data from an unported flow.
- Stop and report any failed permission, token, CI or merge check. Never pretend an issue assignment, build, merge, preview release or ongoing coding session succeeded.

## Starting backlog

First finish `test(public): lock original navigation scopes and legacy aliases` PR #81 if it is not already merged. Next feature: source-grounded Academy trial/enquiry `/join`, with real validation, anti-abuse, PostgreSQL persistence, privacy and integration tests. Source reference `Lefteria-FC` is always read-only. See `docs/implementation/feature-queue.md` and `docs/implementation/original-ui-parity-plan.md` for subsequent tasks.

## Operational check

Open **Actions → Lefteria development director dispatch** and read the latest run. A successful dispatcher run must report the exact assigned issue and `agent-running` label, followed by an actual Copilot-created PR. A green dispatcher job without confirmed assignment is **not** evidence of coding work. Review the Copilot PR in GitHub, not a generated screenshot in ChatGPT.
