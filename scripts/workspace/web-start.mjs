#!/usr/bin/env node
// Workspace web launcher for `yarn start`: locked npm install when needed, then SvelteKit `vite dev` on 0.0.0.0:3000.
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const frontend = resolve(here, '../../frontend');
const conf = Object.fromEntries(readFileSync(join(here, 'workspace.conf'), 'utf8').split('\n')
  .filter((line) => line.includes('=')).map((line) => [line.slice(0, line.indexOf('=')), line.slice(line.indexOf('=') + 1).trim()]));

const lockHash = createHash('sha256').update(readFileSync(join(frontend, 'package-lock.json'))).digest('hex');
const stamp = join(frontend, 'node_modules', '.lefteria-npm-ci.sha256');
if (!existsSync(stamp) || readFileSync(stamp, 'utf8').trim() !== lockHash) {
  console.log('[web] installing locked dependencies with npm ci');
  const install = spawnSync('npm', ['ci', '--no-audit', '--no-fund'], { cwd: frontend, stdio: 'inherit' });
  if (install.status !== 0) process.exit(install.status ?? 1);
  writeFileSync(stamp, lockHash + '\n');
}

const port = process.env.PORT || '3000';
const env = {
  ...process.env,
  HOST: '0.0.0.0',
  PORT: port,
  ORIGIN: conf.LEFTERIA_PUBLIC_ORIGIN,
  LEFTERIA_WEB_EXTRA_ALLOWED_HOSTS: conf.LEFTERIA_WEB_EXTRA_ALLOWED_HOSTS ?? '',
  INTERNAL_API_ORIGIN: conf.LEFTERIA_BRIDGE_ORIGIN
};
const vite = spawn(join(frontend, 'node_modules', '.bin', 'vite'),
  ['dev', '--config', 'vite.workspace.config.ts', '--host', '0.0.0.0', '--port', port, '--strictPort'],
  { cwd: frontend, env, stdio: 'inherit' });
for (const signal of ['SIGTERM', 'SIGINT']) process.on(signal, () => vite.kill(signal));
vite.on('exit', (code, signal) => process.exit(code ?? (signal ? 1 : 0)));

// Warm the on-demand dev compiler so the first visitor is not the one paying for it.
const warm = async () => {
  for (let attempt = 0; attempt < 60; attempt++) {
    try { await fetch(`http://127.0.0.1:${port}/`); break; } catch { await new Promise((r) => setTimeout(r, 1000)); }
  }
  for (const path of ['/', '/app/login', '/admin/login', '/news', '/team', '/academy', '/fixtures', '/contact', '/shop', '/trials', '/app']) {
    try { await fetch(`http://127.0.0.1:${port}${path}`, { signal: AbortSignal.timeout(60000) }); } catch { /* warm-up only */ }
  }
  console.log('[web] dev server warmed');
};
warm();
