import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
const read=p=>readFileSync(new URL('../../'+p,import.meta.url),'utf8');
test('unified CRM connects approved registration to exact canonical Player 360',()=>{
  for(const path of ['lib/admin/AdminFrame.svelte','lib/admin/AdminCrmWorkspaceNav.svelte','lib/admin/AdminRosterPlayersPanel.svelte','lib/admin/AdminPlayerPrivateProfilePanel.svelte','routes/admin/academy-office/+page.svelte','routes/admin/groups/+page.svelte','routes/admin/leads/+page.svelte']){
    const source=read('frontend/src/'+path);
    assert.ok(compile(source,{filename:path,generate:'server'}).js.code.length>100);
  }
  const frame=read('frontend/src/lib/admin/AdminFrame.svelte');
  const crm=frame.slice(frame.indexOf('data-testid="admin-crm-navigation"'));
  for(const id of ['admin-player-crm-link','admin-first-team-players-link','admin-crm-leads-link'])assert.ok(crm.includes(id));
  assert.ok(crm.includes('/admin/academy-office'));
  const office=read('frontend/src/routes/admin/academy-office/+page.svelte');
  assert.match(office,/openedRegistration===row\.id/);
  assert.match(office,/player_id:row\.onboarded_player_id/);
  const roster=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');
  assert.match(roster,/record\.player_id!==requested/);
  assert.match(roster,/verified\(single,currentOwner,0,null\)/);
  assert.match(roster,/selectedPlayer360=requested/);
  const profile=read('frontend/src/lib/admin/AdminPlayerPrivateProfilePanel.svelte');
  assert.match(profile,/profile\.registration\.guardian_phone/);
  assert.match(profile,/profile\.registration\.applicant_birth_date/);
  const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128191000_PlayerRegistrationSource.cs');
  assert.match(sql,/club\.authorize_academy_private_profile\(_digest,_player,false\)/);
  assert.match(sql,/i\.onboarded_player_id=placement\.player_id/);
  assert.match(sql,/placement\.player_id=p\.id/);
  assert.doesNotMatch(sql,/registration_sealed_evidence|evidence_ciphertext|GRANT SELECT/);
});
test('registration PDFs are generated on the server and stored in the same submission transaction',()=>{
  const api=read('backend/src/LefteriaFc.Api/Marketing/AcademyRegistrationEndpoints.cs');
  const renderer=read('backend/src/LefteriaFc.Api/Marketing/AcademyRegistrationPdf.cs');
  assert.match(api,/AcademyRegistrationPdf\.Render\(child,id,policy/);
  assert.match(api,/StoreGeneratedPdfAsync\(connection,transaction,id,uploadDigest/);
  assert.ok(api.indexOf('StoreGeneratedPdfAsync(connection,transaction,id,uploadDigest')<api.indexOf('transaction.CommitAsync'));
  assert.match(api,/'registration_form',NULL::date/);
  assert.match(api,/aes\.Encrypt\(nonce,pdf,ciphertext,tag,aad\)/);
  assert.match(renderer,/SKDocument\.CreatePdf/);
  assert.match(renderer,/DecodeSignature\(child\.SignatureData\)/);
  assert.match(renderer,/codec\.Info\.Width>4096/);
  assert.match(renderer,/RenderSlots\.Wait\(0\)/);
  assert.match(renderer,/child\.ConsentMedia=="yes"/);
  assert.match(renderer,/child\.ParentName/);
  const upload=read('backend/src/LefteriaFc.Api/Marketing/AcademyPublicRegistrationDocumentEndpoints.cs');
  assert.match(upload,/slot is not \("id_front" or "id_back" or "birth_certificate" or "passport_photo"\)/);
  const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128192000_RegistrationFormPdfEscrow.cs');
  assert.match(sql,/WHEN _slot='registration_form' THEN 'registration_form'/);
  assert.match(sql,/WHERE e\.registration_id=NEW\.registration_id/);
  assert.match(sql,/v_doc\.id,NEW\.organization_id,NEW\.player_id/);
  assert.match(sql,/'quarantined'/);
});

test('current contact corrections preserve original applications and existing guardian access rules',()=>{
  const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128193000_AcademyPlayerCurrentContact.cs');
  assert.match(sql,/ENABLE ROW LEVEL SECURITY/);
  assert.match(sql,/authorize_academy_private_profile\(_digest,_player,true\)/);
  assert.match(sql,/player_id=_player AND revision=_expected/);
  assert.match(sql,/operational_contact_only/);
  assert.doesNotMatch(sql,/UPDATE academy_intake\.registrations|INSERT INTO identity\.mobile_identities|UPDATE identity\.family_guardian_permissions/);
  const panel=read('frontend/src/lib/admin/AdminPlayerPrivateProfilePanel.svelte');
  assert.match(panel,/profile\?\.current_contact\?\?/);
  assert.match(panel,/expectedRevision:expected,contact:/);
  assert.match(panel,/result\.player_id!==id/);
  assert.match(panel,/allowWrite&&!editingContact/);
  assert.match(panel,/guardianPhone=\{profile\.registration\.guardian_phone\}/);
  const grants=read('ops/postgres/academy-private-player-profiles-grants.sql');
  assert.match(grants,/has_function_privilege\(m,'club\.write_admin_academy_player_contact/);
  const integration=read('ops/postgres/academy-enrollment-checks.sql');
  assert.match(integration,/Cross-city contact write accepted/);
  assert.match(integration,/Stale contact update accepted/);
});

test('current roster name correction retains original registration and exact identity',()=>{
  const sql=read('backend/src/LefteriaFc.Infrastructure/Persistence/Migrations/20261128194000_AcademyPlayerNameCorrection.cs');
  assert.match(sql,/authorize_academy_private_profile\(_digest,_player,true\)/);
  assert.match(sql,/v_locked_name IS DISTINCT FROM _expected/);
  assert.match(sql,/academy:enrol:/);
  assert.match(sql,/UPDATE club\.players SET name=_name/);
  assert.doesNotMatch(sql,/UPDATE academy_intake\.registrations|UPDATE identity\.|SET id=/);
  const panel=read('frontend/src/lib/admin/AdminPlayerPrivateProfilePanel.svelte');
  assert.match(panel,/nameCorrection:\{expectedName:expected,name\}/);
  assert.match(panel,/result\.official_name!==name/);
  assert.match(panel,/onNameChanged\(name\)/);
  assert.match(panel,/sportingDirty/);
});
test('approved registration opens exact Player 360 documents, with searchable private metadata',()=>{
  const office=read('frontend/src/routes/admin/academy-office/+page.svelte');
  assert.match(office,/view:'players',section:'documents'/);
  const roster=read('frontend/src/lib/admin/AdminRosterPlayersPanel.svelte');
  assert.match(roster,/selectPlayer360Tab\(player,section\)/);
  assert.match(roster,/group_id:membership\.id/);
  const records=read('frontend/src/lib/admin/AdminPlayerPrivateRecordsPanel.svelte');
  assert.ok(compile(records,{filename:'AdminPlayerPrivateRecordsPanel.svelte',generate:'server'}).js.code.length>100);
  assert.match(records,/data-testid="admin-player-document-filters"/);
  assert.match(records,/documentFilter==='registration'&&doc\.document_type==='registration_form'/);
  assert.match(records,/attentionStatuses\.includes\(doc\.status\)/);
  assert.doesNotMatch(records,/downloadUrl|publicMediaUrl/);
});
