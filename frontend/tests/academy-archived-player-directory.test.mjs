import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
test('archive directory is separate, bounded and super-admin only',()=>{
 const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128207000_AcademyArchivedPlayerDirectory.cs');
 for(const marker of ["v_role<>'super_admin'","identity.lock_admin_club_authority","NOT p.is_active OR p.is_archived OR p.is_transferred","LIMIT _limit OFFSET _offset","REVOKE ALL ON FUNCTION"])assert.ok(sql.includes(marker),marker);
 assert.doesNotMatch(sql,/guardian_phone|date_of_birth|UPDATE club\.players|DELETE FROM/);
 const fixture=read('ops/postgres/academy-private-player-profiles-checks.sql');
 for(const marker of ['archive_directory_invalid','active_player_leaked_to_archive','manager_read_archive_directory'])assert.ok(fixture.includes(marker),marker);
});
test('archive panel links existing child to exact finance review',()=>{
 const panel=read('frontend/src/lib/admin/AdminArchivedPlayersPanel.svelte');
 assert.doesNotThrow(()=>compile(panel,{filename:'AdminArchivedPlayersPanel.svelte',generate:'server'}));
 for(const marker of ['admin-archived-player-directory','player_id:row.id','controller.signal.aborted','actorId!==actor'])assert.ok(panel.includes(marker),marker);
});
