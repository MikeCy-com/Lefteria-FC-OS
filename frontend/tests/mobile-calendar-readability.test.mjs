import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { compile } from 'svelte/compiler';

const source = readFileSync(new URL('../src/lib/mobile/components/MobileCalendar.svelte', import.meta.url), 'utf8');

test('mobile calendar compiles and announces the selected date', () => {
  assert.doesNotThrow(() => compile(source, { filename: 'MobileCalendar.svelte', generate:'server' }));
  assert.match(source, /class="date-heading" aria-live="polite" aria-atomic="true"/);
  assert.match(source, /<time datetime=\{selectedDate\}>/);
  assert.match(source, /data-testid="calendar-week-strip"/);
  assert.match(source, /aria-pressed=\{selectedDate === date\}/);
});

test('mobile calendar uses readable event details and 44px month arrows', () => {
  assert.match(source, /\.month-arrow \{ width:44px; min-width:44px/);
  assert.match(source, /\.week-strip small \{ font-size:11px/);
  assert.match(source, /\.event-meta \{[^\n]*font-size:13px; line-height:1\.5/);
  assert.match(source, /\.event-text,\.member-detail p \{[^\n]*font-size:14px; line-height:1\.6/);
  assert.match(source, /\.availability button \{[^\n]*font-size:13px/);
  assert.match(source, /\.reply-state \{[^\n]*font-size:12px/);
  assert.match(source, /<fieldset class="availability" disabled=\{busy \|\| item\.is_cancelled\}/);
});
