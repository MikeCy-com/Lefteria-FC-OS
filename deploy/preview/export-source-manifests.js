/*
 * Run with mongosh --quiet --file /dev/stdin inside a verified isolated source
 * restore container. Redirect stdout to a protected host file, never a terminal.
 *
 * Before invocation the operator MUST verify Docker's exact container ID/image,
 * project label, distinct internal network/volumes, no published ports, read-only
 * backup mount and the backup SHA256. Environment assertions below cannot prove
 * Docker isolation. This script only reads the already-restored database.
 *
 * Required environment:
 * LEFTERIA_EXPORT_DATABASE=lefteria_fc
 * LEFTERIA_EXPORT_CLONE_NAME=lefteria-os-preview-source-restore-<UTC stamp>
 * LEFTERIA_EXPORT_CLONE_ID=<full 64-character Docker container ID>
 * LEFTERIA_EXPORT_CLONE_MARKER=<clone name>:<full container ID>
 * LEFTERIA_EXPORT_BACKUP_SHA256=<verified protected archive SHA256>
 * LEFTERIA_EXPORT_SOURCE_COMMIT=a52d66b8e01c010cfe95ce53fa139e126bae4d84
 * LEFTERIA_EXPORT_TIME_ZONE=Asia/Nicosia
 * Optional LEFTERIA_EXPORT_EXPORTED_AT fixes the UTC export time for a retry.
 *
 * The sole stdout value is a JSON bundle. Each manifests[] entry contains file,
 * kind, sha256, bytes and content (the exact UTF-8 JSON manifest string). Write
 * content without a BOM or newline conversion and verify bytes/hash afterward.
 * The retained BSON archive remains authoritative for original BSON types.
 */
(function sourceManifestExporter() {
  'use strict';

  const crypto = require('crypto');
  const MAXIMUM_BYTES = 32 * 1024 * 1024;
  const SOURCE_REPOSITORY = 'MikeCy-com/Lefteria-FC';
  const SOURCE_COMMIT = 'a52d66b8e01c010cfe95ce53fa139e126bae4d84';
  const COLLECTIONS = Object.freeze({
    roster: ['teams', 'academy_groups', 'players'],
    'staff-relationships': ['staff'],
    accounts: ['mobile_users', 'fans', 'admin_users'],
    calendar: ['fixtures', 'events', 'training_sessions', 'availability', 'event_attendance', 'attendance']
  });
  const SECTION_COLLECTIONS = Object.freeze(['players', 'staff', 'standings', 'venues', 'seasons', 'gallery',
    'sponsors', 'partners', 'marketing_events', 'products', 'trials_settings', 'fixtures', 'teams', 'academy_groups',
    'tickets', 'archived_seasons', 'reviews', 'training_sessions', 'attendance', 'potm_votes', 'club_profile']);
  const ROW_LIMITS = Object.freeze({
    teams: 1000, academy_groups: 2000, players: 20000, staff: 5000,
    mobile_users: 50000, fans: 50000, admin_users: 5000,
    fixtures: 20000, events: 20000, training_sessions: 20000,
    availability: 100000, event_attendance: 100000, attendance: 100000
  });

  function refused() { throw new Error('source_manifest_export_refused'); }
  function sha256(content) { return crypto.createHash('sha256').update(content, 'utf8').digest('hex'); }

  function readConfiguration(environment, now = () => new Date()) {
    const kind = environment.LEFTERIA_EXPORT_KIND ?? 'legacy';
    if (!['legacy', 'public-sections'].includes(kind)) refused();
    const basePublicDigest = environment.LEFTERIA_EXPORT_BASE_PUBLIC_SHA256;
    const snapshotVersion = environment.LEFTERIA_EXPORT_SNAPSHOT_VERSION;
    if (kind === 'public-sections' && (!/^[0-9a-f]{64}$/.test(basePublicDigest ?? '') ||
        !/^[1-9][0-9]{0,5}$/.test(snapshotVersion ?? '') || Number(snapshotVersion) > 100000)) refused();
    const database = environment.LEFTERIA_EXPORT_DATABASE;
    const cloneName = environment.LEFTERIA_EXPORT_CLONE_NAME;
    const cloneId = environment.LEFTERIA_EXPORT_CLONE_ID;
    const backupSha256 = environment.LEFTERIA_EXPORT_BACKUP_SHA256;
    if (database !== 'lefteria_fc' ||
        typeof cloneName !== 'string' || !/^lefteria-os-preview-source-restore-[0-9]{8}t[0-9]{6}z$/.test(cloneName) ||
        typeof cloneId !== 'string' || !/^[0-9a-f]{64}$/.test(cloneId) ||
        environment.LEFTERIA_EXPORT_CLONE_MARKER !== `${cloneName}:${cloneId}` ||
        typeof backupSha256 !== 'string' || !/^[0-9a-f]{64}$/.test(backupSha256) ||
        environment.LEFTERIA_EXPORT_SOURCE_COMMIT !== SOURCE_COMMIT ||
        environment.LEFTERIA_EXPORT_TIME_ZONE !== 'Asia/Nicosia') refused();
    const exportedAt = environment.LEFTERIA_EXPORT_EXPORTED_AT ?? now().toISOString();
    if (typeof exportedAt !== 'string' ||
        !/^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,3})?Z$/.test(exportedAt) ||
        exportedAt.startsWith('0000-') || !Number.isFinite(Date.parse(exportedAt)) ||
        new Date(exportedAt).toISOString().slice(0, 19) !== exportedAt.slice(0, 19)) refused();
    return Object.freeze({ database, cloneName, cloneId, backupSha256, exportedAt, timeZone: 'Asia/Nicosia',
      kind, ...(kind === 'public-sections' ? { basePublicDigest, snapshotVersion: Number(snapshotVersion) } : {}) });
  }

  function safeNumericValue(canonical) {
    if (canonical === null || typeof canonical !== 'object' || Array.isArray(canonical)) return canonical;
    const keys = Object.keys(canonical);
    if (keys.length !== 1) return canonical;
    const key = keys[0];
    const value = canonical[key];
    if (typeof value !== 'string') return canonical;
    if (key === '$numberInt' || key === '$numberLong') {
      if (!/^-?(?:0|[1-9][0-9]*)$/.test(value)) return canonical;
      const number = Number(value);
      if (!Number.isSafeInteger(number) || Object.is(number, -0) || BigInt(number) !== BigInt(value)) return canonical;
      if (key === '$numberInt' && (number < -2147483648 || number > 2147483647)) return canonical;
      return number;
    }
    if (key === '$numberDouble') {
      const number = Number(value);
      if (!/^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$/.test(value) || !Number.isFinite(number) || Object.is(number, -0) ||
          (Number.isInteger(number) && !Number.isSafeInteger(number))) return canonical;
      // JSON preserves these finite IEEE-754 values exactly on reparse. In
      // particular, never collapse -0, non-finite or unsafe integer doubles.
      if (!Object.is(JSON.parse(JSON.stringify(number)), number)) return canonical;
      return number;
    }
    return canonical;
  }

  function bsonType(original) {
    // Real BSON classes expose this brand on their prototype. A document's
    // literal own `_bsontype` field is source data and must not change its shape.
    return original && typeof original === 'object' &&
      !Object.prototype.hasOwnProperty.call(original, '_bsontype') && typeof original._bsontype === 'string'
      ? original._bsontype : null;
  }

  function normalizeCanonicalNumbers(original, canonical) {
    if (typeof original === 'number' ||
        ['Int32', 'Double', 'Long'].includes(bsonType(original))) {
      return safeNumericValue(canonical);
    }
    if (original === null || typeof original !== 'object') {
      if (canonical === undefined) refused(); // Never silently omit a source key.
      return canonical;
    }
    // Other BSON scalars retain their entire canonical EJSON wrapper: Date,
    // ObjectId, Decimal128, binary, timestamp, regular expression, code, etc.
    if (original instanceof Date || bsonType(original)) return canonical;
    if (Array.isArray(original)) {
      if (!Array.isArray(canonical) || canonical.length !== original.length) refused();
      return original.map((value, index) => normalizeCanonicalNumbers(value, canonical[index]));
    }
    if (canonical === null || typeof canonical !== 'object' || Array.isArray(canonical)) refused();
    const originalKeys = Object.keys(original);
    if (originalKeys.length !== Object.keys(canonical).length ||
        originalKeys.some(key => !Object.prototype.hasOwnProperty.call(canonical, key))) refused();
    const result = Object.create(null);
    for (const key of originalKeys) result[key] = normalizeCanonicalNumbers(original[key], canonical[key]);
    // Inspect original BSON values as well as canonical wrappers. A literal
    // source object {"$numberLong":"7"} must remain an object, not become 7.
    return result;
  }

  function serializeRow(row, ejson) {
    if (row === null || typeof row !== 'object' || Array.isArray(row)) refused();
    return normalizeCanonicalNumbers(row, ejson.serialize(row, { relaxed: false }));
  }

  function requireDepth(value, parentDepth = 0) {
    if (value === null || typeof value !== 'object') return;
    const depth = parentDepth + 1;
    if (depth > 32) refused();
    for (const child of Object.values(value)) requireDepth(child, depth);
  }

  function manifest(file, kind, value) {
    requireDepth(value);
    const content = JSON.stringify(value) + '\n';
    const bytes = Buffer.byteLength(content, 'utf8');
    if (bytes === 0 || bytes > MAXIMUM_BYTES) refused();
    return Object.freeze({ file, kind, sha256: sha256(content), bytes, content });
  }

  function buildBundle(configuration, rows) {
    const base = { schema_version: 1, source_repository: SOURCE_REPOSITORY, source_commit: SOURCE_COMMIT };
    for (const name of Object.keys(ROW_LIMITS)) {
      if (!Object.prototype.hasOwnProperty.call(rows, name) || !Array.isArray(rows[name]) ||
          rows[name].length > ROW_LIMITS[name] ||
          rows[name].some(row => row === null || typeof row !== 'object' || Array.isArray(row))) refused();
    }
    const roster = manifest('roster-manifest.json', 'roster', {
      ...base, complete_collections: [...COLLECTIONS.roster],
      teams: rows.teams, academy_groups: rows.academy_groups, players: rows.players
    });
    const staff = manifest('staff-relationships-manifest.json', 'staff-relationships', {
      ...base, roster_sha256: roster.sha256, complete_collections: [...COLLECTIONS['staff-relationships']], staff: rows.staff
    });
    const accounts = manifest('accounts-manifest.json', 'accounts', {
      ...base, roster_sha256: roster.sha256, staff_sha256: staff.sha256, exported_at: configuration.exportedAt,
      complete_collections: [...COLLECTIONS.accounts], mobile_users: rows.mobile_users, fans: rows.fans, admin_users: rows.admin_users
    });
    const calendar = manifest('calendar-manifest.json', 'calendar', {
      ...base, roster_sha256: roster.sha256, exported_at: configuration.exportedAt, time_zone: configuration.timeZone,
      complete_collections: [...COLLECTIONS.calendar], fixtures: rows.fixtures, events: rows.events,
      training_sessions: rows.training_sessions, availability: rows.availability,
      event_attendance: rows.event_attendance, attendance: rows.attendance
    });
    return {
      schema_version: 1,
      purpose: 'isolated-source-manifest-export',
      provenance: {
        source_repository: SOURCE_REPOSITORY, source_commit: SOURCE_COMMIT,
        source_commit_meaning: 'GitHub source-contract baseline; not a claim about the live image revision',
        backup_sha256: configuration.backupSha256, clone_name: configuration.cloneName, clone_id: configuration.cloneId,
        database: configuration.database, exported_at: configuration.exportedAt, time_zone: configuration.timeZone,
        bson_authority: 'Retained protected mongodump archive preserves original BSON types',
        json_encoding: 'Canonical EJSON; only safely round-trippable Int32/Double/Long values become JSON numbers',
        source_consistency: 'Live standalone logical backup; no global point-in-time guarantee',
        collection_order: 'MongoDB _id ascending', source_fields_removed: false
      },
      manifests: [roster, staff, accounts, calendar]
    };
  }

  function buildSectionsBundle(configuration, rows, absentCollections = []) {
    if (configuration.kind !== 'public-sections') refused();
    if (!Array.isArray(absentCollections) || new Set(absentCollections).size !== absentCollections.length ||
        absentCollections.some(name => !SECTION_COLLECTIONS.includes(name) || rows[name]?.length !== 0)) refused();
    for (const name of SECTION_COLLECTIONS) {
      if (!Object.prototype.hasOwnProperty.call(rows, name) || !Array.isArray(rows[name]) || rows[name].length > 20000 ||
          rows[name].some(row => row === null || typeof row !== 'object' || Array.isArray(row))) refused();
    }
    const content = manifest('public-sections-manifest.json', 'public-sections', {
      schema_version: 1, snapshot_version: configuration.snapshotVersion, source_repository: SOURCE_REPOSITORY,
      source_commit: SOURCE_COMMIT, base_public_sha256: configuration.basePublicDigest,
      exported_at: configuration.exportedAt, complete_collections: [...SECTION_COLLECTIONS],
      ...Object.fromEntries(SECTION_COLLECTIONS.map(name => [name, rows[name]]))
    });
    return { schema_version: 1, purpose: 'isolated-public-sections-export',
      provenance: { source_repository: SOURCE_REPOSITORY, source_commit: SOURCE_COMMIT,
        source_commit_meaning: 'Source-contract baseline; not the live image revision',
        backup_sha256: configuration.backupSha256, clone_name: configuration.cloneName, clone_id: configuration.cloneId,
        database: configuration.database, exported_at: configuration.exportedAt, source_fields_removed: false,
        absent_collections: absentCollections, absent_collection_meaning: 'Verified absent in the restored database namespace; exported as empty',
        bson_authority: 'Retained protected mongodump archive preserves original BSON types',
        source_consistency: 'Live standalone logical backup; no global point-in-time guarantee', collection_order: 'MongoDB _id ascending' },
      manifests: [content] };
  }

  async function exportFromMongo(shellDatabase, ejson, environment) {
    // Complete environment validation precedes every database call.
    const configuration = readConfiguration(environment);
    const database = shellDatabase.getSiblingDB(configuration.database);
    const admin = shellDatabase.getSiblingDB('admin');
    if (database.getName() !== 'lefteria_fc') refused();
    const hello = await admin.runCommand({ hello: 1 });
    const parameters = await admin.runCommand({ getParameter: 1, ttlMonitorEnabled: 1 });
    const options = await admin.runCommand({ getCmdLineOpts: 1 });
    if (hello.ok !== 1 || hello.setName || hello.msg === 'isdbgrid' ||
        parameters.ok !== 1 || parameters.ttlMonitorEnabled !== false ||
        options.ok !== 1 || options.parsed?.net?.bindIp !== '127.0.0.1') refused();
    const sections = configuration.kind === 'public-sections';
    const names = sections ? [...SECTION_COLLECTIONS] : Object.keys(ROW_LIMITS);
    const collections = await database.getCollectionInfos({ name: { $in: names } });
    if ((!sections && collections.length !== names.length) || new Set(collections.map(row => row.name)).size !== collections.length ||
        collections.some(row => row.type !== 'collection' || !names.includes(row.name))) refused();
    const absent = names.filter(name => !collections.some(row => row.name === name));
    const rows = Object.create(null);
    for (const name of names) {
      const limit = sections ? 20000 : ROW_LIMITS[name];
      const source = await database.getCollection(name).find({}).sort({ _id: 1 }).limit(limit + 1).toArray();
      if (source.length > limit || (absent.includes(name) && source.length !== 0)) refused();
      rows[name] = source.map(row => serializeRow(row, ejson));
    }
    return sections ? buildSectionsBundle(configuration, rows, absent) : buildBundle(configuration, rows);
  }

  const exported = { MAXIMUM_BYTES, SOURCE_COMMIT, SECTION_COLLECTIONS, readConfiguration, normalizeCanonicalNumbers, serializeRow, manifest, buildBundle, buildSectionsBundle, exportFromMongo };
  if (typeof module !== 'undefined' && module.exports) module.exports = exported;
  if (typeof db !== 'undefined' && typeof EJSON !== 'undefined') {
    // Return the promise as the script completion value so mongosh waits for all
    // reads and emits only the complete bundle, never partial manifest output.
    return exportFromMongo(db, EJSON, process.env).then(bundle => { print(JSON.stringify(bundle)); }).catch(() => {
      console.error('source_manifest_export_failed');
      quit(2);
    });
  }
})();
