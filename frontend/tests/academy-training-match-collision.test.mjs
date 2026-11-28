import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
test('bidirectional draft collision checks share the existing group/day transaction lock',()=>{
 const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128204000_AcademyTrainingMatchCollision.cs');
 for(const marker of ["NEW.organization_id||':'||NEW.group_id||':'||NEW.training_day::text","NEW.organization_id||':'||NEW.academy_group_id||':'||NEW.match_date::text","m.academy_group_id=NEW.group_id","p.group_id=NEW.academy_group_id","USING ERRCODE='23505'","REVOKE ALL ON FUNCTION club.reject_academy_match_training_kickoff() FROM PUBLIC"])assert.ok(sql.includes(marker),marker);
 assert.equal((sql.match(/pg_advisory_xact_lock/g)||[]).length,2);
 assert.doesNotMatch(sql,/INSERT INTO|UPDATE club\.|DELETE FROM|GRANT EXECUTE/);
 const fixture=read('ops/postgres/academy-training-plans-lifecycle-checks.sql');
 for(const marker of ['match_kickoff_inside_training_accepted','training_covering_existing_match_accepted',"'kickoff_local','18:15'"])assert.ok(fixture.includes(marker),marker);
});
