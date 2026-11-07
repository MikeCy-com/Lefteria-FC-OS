import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const read = (path) => readFileSync(new URL('../../' + path, import.meta.url), 'utf8');

test('director is GitHub-event-driven, strictly OS-scoped and never deploys', () => {
  const workflow = read('.github/workflows/development-director.yml');
  const dispatcher = read('ops/agents/dispatch-next.sh');
  assert.match(workflow, /workflow_run:/);
  assert.match(workflow, /issues:/);
  assert.match(workflow, /workflow_dispatch:/);
  assert.doesNotMatch(workflow, /schedule:|cron:/);
  assert.match(workflow, /head_branch == 'main'/);
  assert.match(workflow, /conclusion == 'success'/);
  assert.match(workflow, /github\.repository == 'MikeCy-com\/Lefteria-FC-OS'/);
  assert.match(workflow, /persist-credentials: false/);
  assert.match(workflow, /secrets\.LEFTERIA_COPILOT_ASSIGN_TOKEN/);
  assert.doesNotMatch(workflow, /uses:.*ssh-action|docker login|^\s+push:|^\s+contents: write/im);
  assert.match(dispatcher, /expected_repo="MikeCy-com\/Lefteria-FC-OS"/);
  assert.match(dispatcher, /agent-running/);
  assert.match(dispatcher, /agent-ready/);
  assert.match(dispatcher, /copilot-swe-agent\[bot\]/);
  assert.match(dispatcher, /custom_agent: "lefteria-developer"/);
  assert.match(dispatcher, /assigned" != "true"/);
  assert.doesNotMatch(dispatcher, /^\s*(?:sudo|ssh|scp)\s+|docker compose|git push/im);
});

test('director and developer forbid modifying original or live production', () => {
  for (const file of [
    '.github/agents/lefteria-director.agent.md',
    '.github/agents/lefteria-developer.agent.md'
  ]) {
    const policy = read(file);
    assert.match(policy, /MikeCy-com\/Lefteria-FC-OS/);
    assert.match(policy, /MikeCy-com\/Lefteria-FC/);
    assert.match(policy, /lefteriafc\.cy/);
    assert.match(policy, /PostgreSQL/);
    assert.match(policy, /five|all five/i);
  }
});
