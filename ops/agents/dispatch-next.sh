#!/usr/bin/env bash
# Event-driven, single-flight queue handoff to GitHub Copilot cloud agent.
# No deployment, merge, SSH or production permissions.
set -Eeuo pipefail

expected_repo="MikeCy-com/Lefteria-FC-OS"
if [[ "${GITHUB_REPOSITORY:-}" != "$expected_repo" ]]; then
  echo "::error::Refusing to dispatch outside $expected_repo"
  exit 1
fi

if [[ -z "${GH_TOKEN:-}" ]]; then
  echo "::warning::Event-driven coding dispatch is not activated: configure the LEFTERIA_COPILOT_ASSIGN_TOKEN Actions secret with a Copilot-authorized user token."
  echo "No issue was assigned; no coding session was started." >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
  exit 0
fi

for tool in gh jq; do
  command -v "$tool" >/dev/null || { echo "::error::Missing $tool"; exit 1; }
done

# A single open task already assigned to a coding agent blocks new assignments.
# Closed issues are omitted so the next queued task may run after the prior
# Copilot PR merges and closes its linked issue.
running="$(gh api --paginate "/repos/$expected_repo/issues?state=open&labels=agent-running&per_page=100" \
  --jq '.[] | select(.pull_request == null) | .number' | sed -n '1p')"
if [[ -n "$running" ]]; then
  echo "Existing active coding issue #$running; no duplicate assignment."
  echo "Existing coding issue #$running remains active; no additional task dispatched." >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
  exit 0
fi

# Issues are prioritized by creation time (GitHub's default sort), and only
# explicit agent-ready issues can be assigned. Never inspect PRs as queue items.
next="$(gh api --paginate "/repos/$expected_repo/issues?state=open&labels=agent-ready&sort=created&direction=asc&per_page=100" \
  --jq '.[] | select(.pull_request == null) | .number' | sed -n '1p')"
if [[ -z "$next" ]]; then
  echo "No open agent-ready implementation issue; no automatic feature invented."
  echo "No queued implementation issue. Development Director must add a source-grounded agent-ready task." >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
  exit 0
fi

# This assignment requires the USER-to-server token of a Copilot-enabled user;
# the regular GITHUB_TOKEN installation token cannot start Copilot coding tasks.
# The custom agent is restricted by repo instructions and may only create PRs.
assignment="$(jq -n --arg repository "$expected_repo" '{
  assignees: ["copilot-swe-agent[bot]"],
  agent_assignment: {
    target_repo: $repository,
    base_branch: "main",
    custom_agent: "lefteria-developer"
  }
}')"
printf '%s' "$assignment" | gh api --method POST \
  -H "Accept: application/vnd.github+json" \
  -H "X-GitHub-Api-Version: 2022-11-28" \
  "/repos/$expected_repo/issues/$next/assignees" --input - >/dev/null

# Do not claim success if GitHub silently ignores a non-assignable Copilot bot.
assigned="$(gh api "/repos/$expected_repo/issues/$next" \
  --jq '[.assignees[].login] | any(. == "copilot-swe-agent[bot]" or . == "copilot-swe-agent")')"
if [[ "$assigned" != "true" ]]; then
  echo "::error::GitHub did not confirm Copilot assignment of issue #$next."
  exit 1
fi

# Mark only after GitHub confirms assignment; a failed label update leaves an
# assigned issue visible in GitHub, so the subsequent run must inspect it.
gh api --method POST "/repos/$expected_repo/issues/$next/labels" \
  -f 'labels[]=agent-running' >/dev/null
gh api --method DELETE "/repos/$expected_repo/issues/$next/labels/agent-ready" >/dev/null

echo "Assigned issue #$next to Copilot's Lefteria Developer profile."
echo "Dispatched #$next to GitHub Copilot cloud agent. Code changes require reviewed PRs and five green CI checks; no live production access." >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
