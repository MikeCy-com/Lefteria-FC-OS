import test from 'node:test';
import assert from 'node:assert/strict';
import { readdirSync, readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';

const frontend = resolve(import.meta.dirname, '../src');
const backend = resolve(import.meta.dirname, '../../backend/src');
const sql = resolve(import.meta.dirname, '../../ops/postgres');
const activeTypes = /\.(?:svelte|css|ts|js|cs|sql)$/i;
const forbidden = /#f5a623(?:[0-9a-f]{2})?|\b245\s*,\s*166\s*,\s*35\b|\b245\s+166\s+35\b/i;

function scan(root, results = []) {
  for (const item of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, item.name);
    if (item.isDirectory()) {
      // Historic EF migrations are append-only evidence of their original
      // schema; the new migration changes active database defaults.
      if (path === join(backend, 'LefteriaFc.Infrastructure', 'Persistence', 'Migrations')) continue;
      scan(path, results);
    } else if (item.isFile() && activeTypes.test(path)) {
      const text = readFileSync(path, 'utf8');
      if (forbidden.test(text)) results.push(path);
    }
  }
  return results;
}

test('all active Lefteria FC screens and backend visuals use the new gold', () => {
  assert.deepEqual([...scan(frontend), ...scan(backend), ...scan(sql)], [],
    'Retired #f5a623 or its RGB equivalent remains in active source.');
  const birthdayMigration = readFileSync(join(backend, 'LefteriaFc.Infrastructure/Persistence/Migrations/20261114193000_BrandGoldAccent.cs'), 'utf8');
  const invoice = readFileSync(join(backend, 'LefteriaFc.Api/Payabl/AcademyInvoicePdf.cs'), 'utf8');
  assert.match(birthdayMigration, /ALTER COLUMN background_color SET DEFAULT '#F6BB38'/);
  assert.match(birthdayMigration, /ELSE '#F6BB38' END/);
  assert.ok(invoice.includes('Gold = Color(246,187,56)'));
  const css = readFileSync(join(frontend, 'lib/public/public.css'), 'utf8');
  const admin = readFileSync(join(frontend, 'lib/admin/admin.css'), 'utf8');
  const mobile = readFileSync(join(frontend, 'lib/mobile/components/mobile-theme.css'), 'utf8');
  for (const [name, source, token] of [
    ['public', css, '--public-gold:var(--lf-color-accent,#f6bb38)'],
    ['admin', admin, '--admin-gold:#f6bb38'],
    ['mobile', mobile, '--lf-gold: #f6bb38']
  ]) assert.ok(source.includes(token), name + ' gold token is missing');
});
