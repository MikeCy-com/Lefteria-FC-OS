import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';

const require = createRequire(import.meta.url);
const exporter = require('./export-source-manifests.js');
const cloneName = 'lefteria-os-preview-source-restore-20261031t174927z';
const cloneId = 'e'.repeat(64);
const environment = {
  LEFTERIA_EXPORT_DATABASE: 'lefteria_fc',
  LEFTERIA_EXPORT_CLONE_NAME: cloneName,
  LEFTERIA_EXPORT_CLONE_ID: cloneId,
  LEFTERIA_EXPORT_CLONE_MARKER: `${cloneName}:${cloneId}`,
  LEFTERIA_EXPORT_BACKUP_SHA256: 'a'.repeat(64),
  LEFTERIA_EXPORT_SOURCE_COMMIT: exporter.SOURCE_COMMIT,
  LEFTERIA_EXPORT_TIME_ZONE: 'Asia/Nicosia',
  LEFTERIA_EXPORT_EXPORTED_AT: '2026-10-31T18:00:00.123Z'
};
const collections = ['teams', 'academy_groups', 'players', 'staff', 'mobile_users', 'fans', 'admin_users',
  'fixtures', 'events', 'training_sessions', 'availability', 'event_attendance', 'attendance'];
const emptyRows = () => Object.fromEntries(collections.map(name => [name, []]));
const bson = type => Object.create({ _bsontype: type });

test('source export refuses incomplete clone assertions before any database call', async () => {
  for (const field of Object.keys(environment)) {
    if (field === 'LEFTERIA_EXPORT_EXPORTED_AT') continue;
    let queries = 0;
    const shell = { getSiblingDB() { queries++; throw new Error('Unexpected query'); } };
    await assert.rejects(exporter.exportFromMongo(shell, {}, { ...environment, [field]: undefined }),
      /source_manifest_export_refused/);
    assert.equal(queries, 0);
  }
  for (const invalid of [
    { LEFTERIA_EXPORT_DATABASE: 'production' },
    { LEFTERIA_EXPORT_CLONE_NAME: 'lefteria-mongo' },
    { LEFTERIA_EXPORT_CLONE_ID: 'short' },
    { LEFTERIA_EXPORT_CLONE_MARKER: `${cloneName}:${'b'.repeat(64)}` },
    { LEFTERIA_EXPORT_SOURCE_COMMIT: '0'.repeat(40) },
    { LEFTERIA_EXPORT_TIME_ZONE: 'UTC' }
  ]) assert.throws(() => exporter.readConfiguration({ ...environment, ...invalid }), /source_manifest_export_refused/);
});

test('export timestamps require a real UTC calendar instant', () => {
  for (const value of ['2026-02-30T12:00:00Z', '2026-13-01T12:00:00Z', '0000-01-01T00:00:00Z',
    '2026-10-31T18:00:00+02:00', '2026-10-31T24:00:00Z', '2026-10-31T18:00:00.1234567Z']) {
    assert.throws(() => exporter.readConfiguration({ ...environment, LEFTERIA_EXPORT_EXPORTED_AT: value }),
      /source_manifest_export_refused/);
  }
  assert.equal(exporter.readConfiguration(environment).exportedAt, environment.LEFTERIA_EXPORT_EXPORTED_AT);
});

test('safe BSON numeric values become numbers while unsafe and special values retain wrappers', () => {
  const normalize = exporter.normalizeCanonicalNumbers;
  assert.equal(normalize(bson('Int32'), { $numberInt: '36' }), 36);
  assert.equal(normalize(bson('Long'), { $numberLong: '9007199254740991' }), Number.MAX_SAFE_INTEGER);
  assert.equal(normalize(bson('Double'), { $numberDouble: '1.25' }), 1.25);
  assert.equal(normalize(0.0000001, { $numberDouble: '1e-7' }), 0.0000001);
  for (const [type, wrapper] of [
    ['Long', { $numberLong: '9007199254740993' }], ['Long', { $numberLong: '-9007199254740993' }],
    ['Double', { $numberDouble: '-0.0' }], ['Double', { $numberDouble: 'NaN' }],
    ['Double', { $numberDouble: 'Infinity' }], ['Double', { $numberDouble: '-Infinity' }],
    ['Double', { $numberDouble: '1e20' }], ['Decimal128', { $numberDecimal: '1.25' }]
  ]) assert.deepEqual(normalize(bson(type), wrapper), wrapper);
});

test('dates, ObjectIds and binary keep complete canonical EJSON wrappers', () => {
  const values = [
    [new Date(0), { $date: { $numberLong: '0' } }],
    [bson('ObjectId'), { $oid: '000000000000000000000001' }],
    [bson('Binary'), { $binary: { base64: 'AAEC', subType: '00' } }],
    [bson('Timestamp'), { $timestamp: { t: 3, i: 1 } }]
  ];
  for (const [original, canonical] of values) assert.deepEqual(exporter.normalizeCanonicalNumbers(original, canonical), canonical);
});

test('literal EJSON-looking source objects and all private or unknown fields remain whole', () => {
  const original = JSON.parse('{"id":" untouched ","title":"Ελληνικά & café","numeric_string":"007","literal":{"$numberLong":"7"},"branded":{"_bsontype":"Long","value":"kept"},"__proto__":{"retained":true},"private_notes":"fictional"}');
  const result = exporter.serializeRow(original, { serialize: value => JSON.parse(JSON.stringify(value)) });
  assert.equal(JSON.stringify(result), JSON.stringify(original));
  assert.equal(Object.getPrototypeOf(result), null);
  assert.equal(Object.prototype.hasOwnProperty.call(result, '__proto__'), true);
  assert.throws(() => exporter.normalizeCanonicalNumbers({ omitted: undefined }, {}), /source_manifest_export_refused/);
});

test('four bounded exact-byte manifests retain complete collections and bind predecessor hashes', () => {
  const rows = emptyRows();
  rows.players.push({ id: 'fictional', number: 7, unknown: { note: 'Ακέραιο & "quoted"' } });
  const bundle = exporter.buildBundle(exporter.readConfiguration(environment), rows);
  assert.equal(bundle.manifests.length, 4);
  for (const entry of bundle.manifests) {
    assert.equal(Buffer.byteLength(entry.content, 'utf8'), entry.bytes);
    assert.equal(createHash('sha256').update(entry.content, 'utf8').digest('hex'), entry.sha256);
    assert.equal(entry.content.at(-1), '\n');
    assert.equal(entry.content.codePointAt(0), '{'.codePointAt(0));
  }
  const [rosterEntry, staffEntry, accountsEntry, calendarEntry] = bundle.manifests;
  const [roster, staff, accounts, calendar] = bundle.manifests.map(row => JSON.parse(row.content));
  assert.deepEqual(Object.keys(roster), ['schema_version', 'source_repository', 'source_commit', 'complete_collections', 'teams', 'academy_groups', 'players']);
  assert.deepEqual(Object.keys(staff), ['schema_version', 'source_repository', 'source_commit', 'roster_sha256', 'complete_collections', 'staff']);
  assert.equal(roster.exported_at, undefined);
  assert.equal(staff.exported_at, undefined);
  assert.equal(staff.roster_sha256, rosterEntry.sha256);
  assert.equal(accounts.roster_sha256, rosterEntry.sha256);
  assert.equal(accounts.staff_sha256, staffEntry.sha256);
  assert.equal(calendar.roster_sha256, rosterEntry.sha256);
  assert.equal(accounts.exported_at, environment.LEFTERIA_EXPORT_EXPORTED_AT);
  assert.equal(calendar.time_zone, 'Asia/Nicosia');
  assert.deepEqual(calendar.complete_collections, collections.slice(7));
  assert.deepEqual(roster.players[0], rows.players[0]);
  assert.deepEqual(bundle.manifests.map(row => row.kind), ['roster', 'staff-relationships', 'accounts', 'calendar']);
  assert.ok(accountsEntry.bytes > 0 && calendarEntry.bytes > 0);
  assert.match(bundle.provenance.source_commit_meaning, /not a claim about the live image revision/);
});

test('missing collections, excessive depth and UTF-8 byte bounds fail without truncation', () => {
  const rows = emptyRows();
  delete rows.events;
  assert.throws(() => exporter.buildBundle(exporter.readConfiguration(environment), rows), /source_manifest_export_refused/);
  let nested = {};
  for (let depth = 0; depth < 33; depth++) nested = { child: nested };
  assert.throws(() => exporter.manifest('fixture.json', 'roster', nested), /source_manifest_export_refused/);
  assert.throws(() => exporter.manifest('fixture.json', 'roster', { text: '€'.repeat(Math.ceil(exporter.MAXIMUM_BYTES / 3)) }),
    /source_manifest_export_refused/);
});

test('database reads use every complete collection in stable Mongo _id order', async () => {
  const reads = [];
  const target = {
    getName: () => 'lefteria_fc',
    getCollectionInfos: async () => collections.map(name => ({ name, type: 'collection' })),
    getCollection(name) {
      const record = { name };
      reads.push(record);
      const cursor = {
        find(filter) { record.filter = filter; return cursor; },
        sort(sort) { record.sort = sort; return cursor; },
        limit(limit) { record.limit = limit; return cursor; },
        toArray: async () => []
      };
      return cursor;
    }
  };
  const admin = { async runCommand(command) {
    if (command.hello) return { ok: 1, isWritablePrimary: true };
    if (command.getParameter) return { ok: 1, ttlMonitorEnabled: false };
    if (command.getCmdLineOpts) return { ok: 1, parsed: { net: { bindIp: '127.0.0.1' } } };
    throw new Error('Unexpected command');
  } };
  const shell = { getSiblingDB: name => name === 'admin' ? admin : target };
  const bundle = await exporter.exportFromMongo(shell, {}, environment);
  assert.equal(bundle.manifests.length, 4);
  assert.deepEqual(reads.map(row => row.name), collections);
  for (const row of reads) {
    assert.deepEqual(row.filter, {});
    assert.deepEqual(row.sort, { _id: 1 });
    assert.ok(row.limit > 0);
  }
  admin.runCommand = async command => command.getParameter ? { ok: 1, ttlMonitorEnabled: true } :
    command.hello ? { ok: 1 } : { ok: 1, parsed: { net: { bindIp: '127.0.0.1' } } };
  reads.length = 0;
  await assert.rejects(exporter.exportFromMongo(shell, {}, environment), /source_manifest_export_refused/);
  assert.equal(reads.length, 0);
});
