import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=(file)=>readFileSync(new URL('../../'+file,import.meta.url),'utf8');
test('Academy writer uses only scoped stored operation and parameterized inputs',()=>{
  const adapter=source('backend/src/LefteriaFc.Infrastructure/Marketing/PostgresAcademyLeadWriter.cs');
  const registry=source('backend/src/LefteriaFc.Infrastructure/InfrastructureServiceCollectionExtensions.cs');
  assert.ok(adapter.includes('SELECT marketing.store_academy_lead('));
  assert.ok(adapter.includes('organization!="lefteria_fc_academy"'));
  assert.ok(adapter.includes('trustedFingerprint.Length!=64'));
  assert.ok(adapter.includes('catch(NpgsqlException){return AcademyLeadStoreResult.Unavailable;}'));
  assert.ok(adapter.includes('"stored"=>AcademyLeadStoreResult.Stored'));
  assert.ok(adapter.includes('"rate_limited"=>AcademyLeadStoreResult.RateLimited'));
  assert.doesNotMatch(adapter,/\bINSERT INTO\b|\bUPDATE\s+marketing\b|\bGRANT\b|X-Forwarded-For/);
  assert.ok(registry.includes('AddScoped<IAcademyLeadWriter, PostgresAcademyLeadWriter>()'));
});
test('Academy writer is not an exposed public HTTP endpoint',()=>{
  const adapter=source('backend/src/LefteriaFc.Infrastructure/Marketing/PostgresAcademyLeadWriter.cs');
  assert.doesNotMatch(adapter,/MapPost\(|MapGroup\(|HttpContext|Twilio|Hostinger|\.ExecuteSqlRaw/);
  assert.ok(adapter.includes('NpgsqlDbType.Text'));
  assert.ok(adapter.includes('DBNull.Value'));
});
