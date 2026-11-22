import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {brandOwnerForRole,publicLogoUrl,readPublicBrand} from '../src/lib/mobile/club-branding.ts';

const read=(p)=>readFileSync(new URL('../src/'+p,import.meta.url),'utf8');
const settings=read('routes/admin/settings/+page.svelte');
const editor=read('lib/admin/ClubEditor.svelte');
const mobile=read('routes/app/[...path]/+page.svelte');
const mobileShell=read('lib/mobile/components/MobileShell.svelte');
const publicContent=read('lib/server/public-content.ts');
const navCatalog=read('lib/admin/module-catalog.ts');
const nav=read('lib/admin/AdminFrame.svelte');

test('club profile is managed centrally, preserving existing audited per-org save',()=>{
  for(const [name,source] of [['settings',settings],['editor',editor],['mobile',mobile],['mobile-shell',mobileShell]])
    assert.ok(compile(source,{filename:name+'.svelte',generate:'server'}).js.code.length>100);
  assert.match(settings,/data-testid="admin-settings-club"/);
  assert.match(settings,/href="\/admin\/club"/);
  assert.match(editor,/club-organization|club-profile-target/);
  assert.match(editor,/club-save/);
  assert.match(editor,/club-social-/);
  assert.match(editor,/keys: \['logo_url', 'stadium_image_url'\]/);
  assert.match(editor,/club-back-to-settings/);
  assert.match(navCatalog,/id: 'operations'/);
  assert.match(navCatalog,/id: 'club', name: 'Ταυτότητα & επικοινωνία συλλόγου'/);
  assert.match(nav,/\['settings','club','staff','users','onboarding'\]/);
});

test('public shell always uses the selected public organization rather than inherited First Team identity',()=>{
  assert.match(publicContent,/branding: result\.value\?\.club \?\? null/);
  assert.doesNotMatch(publicContent,/brandingTask = organization === 'lefteria_fc'/);
  assert.match(publicContent,/requestSite\('lefteria_fc_academy'\)/);
});

test('mobile header consumes verified public-only club name and logo from role or management organization',()=>{
  assert.equal(brandOwnerForRole('fan',null),'lefteria_fc');
  assert.equal(brandOwnerForRole('parent',null),'lefteria_fc_academy');
  assert.equal(brandOwnerForRole('player',null),'lefteria_fc_academy');
  assert.equal(brandOwnerForRole('coach',null),'lefteria_fc_academy');
  assert.equal(brandOwnerForRole('management','lefteria_fc'),'lefteria_fc');
  assert.equal(brandOwnerForRole('management','lefteria_fc_academy'),'lefteria_fc_academy');
  assert.equal(brandOwnerForRole('management',null),null);
  assert.equal(publicLogoUrl('javascript:alert(1)'),null);
  assert.equal(publicLogoUrl('//evil.invalid/logo.svg'),null);
  assert.equal(publicLogoUrl('https://owner.invalid/logo.png'),'https://owner.invalid/logo.png');
  assert.equal(readPublicBrand({club:{organization_id:'lefteria_fc',name:'FC',logo_url:'/fc.png'}},'lefteria_fc_academy'),null);
  const brand=readPublicBrand({club:{organization_id:'lefteria_fc_academy',name:'Academy',logo_url:'/academy.png'}},'lefteria_fc_academy');
  assert.equal(brand?.name,'Academy');
  assert.equal(brand?.logoUrl,'/academy.png');
  assert.match(mobile,/credentials:'omit',cache:'no-store'/);
  assert.match(mobile,/readPublicBrand\(raw,owner\)/);
  assert.match(mobile,/clubLogoUrl=\{publicClubBrand\?\.logoUrl\?\?null\}/);
  assert.match(mobile,/data-testid="mobile-club-contact"/);
  assert.match(mobile,/data-testid="mobile-club-email"/);
  assert.match(mobile,/data-testid="mobile-club-phone"/);
  assert.match(mobile,/mobile-club-social-/);
  assert.equal(brand?.email,null);
  assert.deepEqual(brand?.socialLinks,{});
  const publicContacts=readPublicBrand({club:{organization_id:'lefteria_fc',name:'FC',
    logo_url:'/fc.png',email:'info@example.org',phone:'+357 12345678',
    social_links:{facebook:'https://facebook.com/sample',youtube:'javascript:alert(1)'}}},'lefteria_fc');
  assert.equal(publicContacts?.email,'info@example.org');
  assert.equal(publicContacts?.phone,'+357 12345678');
  assert.equal(publicContacts?.socialLinks.facebook,'https://facebook.com/sample');
  assert.equal(publicContacts?.socialLinks.youtube,undefined);
  assert.match(mobileShell,/data-testid="mobile-club-brand-name"/);
  assert.doesNotMatch(mobileShell,/src="\/mobile\/club-logo\.png"/);
});


test('public contact email accepts ordinary addresses and rejects malformed or injected links',()=>{
  for(const email of ['sales@example.org','info+club@example.org'])
    assert.equal(readPublicBrand({club:{organization_id:'lefteria_fc',name:'FC',email}},'lefteria_fc')?.email,email);
  for(const email of ['info example.org','info@ example.org','info@example.org?subject=test','info@example.org\n','javascript:info@example.org'])
    assert.equal(readPublicBrand({club:{organization_id:'lefteria_fc',name:'FC',email}},'lefteria_fc')?.email,null);
});

test('public page loader rejects cross-organization site responses and Academy navigation',()=>{
  assert.match(publicContent,/export function verifiedPublicSite\(/);
  assert.match(publicContent,/site\?\.club\?\.organization_id === organization \? site : null/);
  assert.match(publicContent,/verifiedPublicSite\(result\.value, organization\)/);
  assert.match(publicContent,/verifiedPublicSite\(navigation\.value, 'lefteria_fc_academy'\)/);
  assert.match(publicContent,/branding: site\?\.club \?\? null/);
  assert.match(publicContent,/navigationGroups: academyNavigation\?\.academy_groups \?\? \[\]/);
});
