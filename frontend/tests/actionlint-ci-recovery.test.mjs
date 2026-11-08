import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const workflow = readFileSync(new URL(
  '../../.github/workflows/rewrite-build.yml', import.meta.url), 'utf8');

test('workflow syntax gate always executes pinned actionlint, including release CDN outage', () => {
  assert.ok(workflow.includes('name: Validate all workflow syntax and expression contexts'));
  assert.ok(workflow.includes('actionlint_1.7.12_linux_amd64.tar.gz'));
  assert.ok(workflow.includes('8aca8db96f1b94770f1b0d72b6dddcb1ebb8123cb3712530b08cc387b349a3d8'));
  assert.ok(workflow.includes('sha256sum --check --strict'));
  assert.ok(workflow.includes('--retry-all-errors'));
  assert.ok(workflow.includes('go install github.com/rhysd/actionlint/cmd/actionlint@v1.7.12'));
  assert.ok(workflow.includes('GOSUMDB=sum.golang.org GOPROXY=https://proxy.golang.org'));
  assert.ok(workflow.includes('"$RUNNER_TEMP/actionlint" -shellcheck= -pyflakes='));
  assert.doesNotMatch(workflow,/GOSUMDB=off|GOINSECURE|actionlint@latest|\|\|\s*true/);
});
