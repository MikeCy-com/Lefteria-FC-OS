import test from 'node:test';
import assert from 'node:assert/strict';
import { clubDay, nextPublicFixture, fixtureCountdown, snapshotInstant } from '../src/lib/public/fixture-clock.ts';
const fixture = (id,date,time,kickoff,extra={}) => ({id,status:'Scheduled',match_date:date,match_time:time,kickoff_at:kickoff,...extra});

test('next fixture uses Cyprus midnight while UTC and viewer date may still be yesterday', () => {
  const dated = fixture('day-only','2026-10-31',null,null);
  assert.equal(clubDay(Date.parse('2026-10-31T21:59:59Z')),'2026-10-31');
  assert.equal(nextPublicFixture([dated],Date.parse('2026-10-31T21:59:59Z')),dated);
  assert.equal(clubDay(Date.parse('2026-10-31T22:00:00Z')),'2026-11-01');
  assert.equal(nextPublicFixture([dated],Date.parse('2026-10-31T22:00:00Z')),undefined);
  assert.equal(clubDay(Date.parse('2026-07-01T21:00:00Z')),'2026-07-02');
});

test('known kickoff is strictly future with stable same-day time/id order and no false completed match', () => {
  const late = fixture('late','2026-11-01','18:00','2026-11-01T16:00:00Z');
  const early = fixture('b','2026-11-01','14:30:00','2026-11-01T12:30:00Z');
  const equal = {...early,id:'a'};
  const dayOnly = fixture('unknown','2026-11-01',null,null);
  assert.equal(nextPublicFixture([late,early,dayOnly,equal],Date.parse('2026-11-01T12:29:59Z')).id,'a');
  assert.equal(nextPublicFixture([early],Date.parse('2026-11-01T12:30:00Z')),undefined);
  assert.equal(nextPublicFixture([late,early],Date.parse('2026-11-01T12:30:00Z')),late);
  for (const status of ['Live','Half Time','Completed','Postponed','Cancelled'])
    assert.equal(nextPublicFixture([{...late,status}],Date.parse('2026-11-01T00:00:00Z')),undefined);
});

test('missing time stays date-only with no countdown; invalid/ambiguous kickoff metadata never guesses an offset', () => {
  const dayOnly = fixture('day','2026-11-01',null,null);
  assert.equal(nextPublicFixture([dayOnly],Date.parse('2026-11-01T21:59:59Z')),dayOnly);
  assert.equal(fixtureCountdown(dayOnly,Date.parse('2026-11-01T12:00:00Z')),null);
  for (const bad of [fixture('fold','2026-10-25','03:30',null),fixture('gap','2026-03-29','03:30',null),fixture('empty','2026-11-01','',null),fixture('unaware','2026-11-01','14:30','2026-11-01T14:30:00'),fixture('invalid','2026-11-01','bad',null),fixture('bad-metadata','2026-11-01',null,'invalid')])
    assert.equal(nextPublicFixture([bad],Date.parse('2026-03-01T00:00:00Z')),undefined);
  assert.equal(snapshotInstant({time_zone:'UTC',generated_at:'2026-11-01T00:00:00Z'}),null);
  assert.equal(snapshotInstant({time_zone:'Asia/Nicosia',generated_at:'invalid'}),null);
});

test('countdown follows authoritative UTC instant across DST and is independent of viewer timezone', () => {
  const match = fixture('match','2026-11-01','14:30','2026-11-01T12:30:00Z');
  const now = Date.parse('2026-10-31T12:00:00Z');
  assert.deepEqual(fixtureCountdown(match,now),[1,0,30,0]);
  assert.equal(fixtureCountdown(match,Date.parse('2026-11-01T12:30:00Z')),null);
  const old = process.env.TZ;
  try { for(const zone of ['Pacific/Honolulu','Asia/Nicosia','Pacific/Kiritimati']) { process.env.TZ=zone;
    assert.equal(nextPublicFixture([match],now),match);
    assert.deepEqual(fixtureCountdown(match,now),[1,0,30,0]);
    assert.equal(clubDay(Date.parse('2026-10-31T22:00:00Z')),'2026-11-01');
  } } finally { if(old===undefined)delete process.env.TZ;else process.env.TZ=old; }
  assert.equal(snapshotInstant({time_zone:'Asia/Nicosia',generated_at:'2026-11-01T00:00:00Z'}),Date.parse('2026-11-01T00:00:00Z'));
});
