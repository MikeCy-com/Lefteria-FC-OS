import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {ONBOARDING_VERSION,mobileGuide,adminGuide} from '../src/lib/onboarding/guides.ts';
import {normalizeTab} from '../src/lib/mobile/navigation.ts';

const source = file => readFileSync(new URL(file,import.meta.url),'utf8');
const mobileRoles=['fan','player','parent','coach','management'];

test('every mobile role has a versioned own-role profile-first walkthrough',()=>{
  assert.match(ONBOARDING_VERSION,/^20\d\d\.\d\d\.\d\d\.\d+$/);
  for(const role of mobileRoles){
    const guide=mobileGuide({role,first_name:'',last_name:'',birthday:null});
    assert.equal(guide[0].id,'profile',role);
    assert.equal(guide[0].state,'ready',role);
    assert.equal(guide[0].destination,'/app?tab=profile',role);
    assert.ok(guide.length>=3,role);
    assert.equal(new Set(guide.map(x=>x.id)).size,guide.length,role);
    for(const step of guide){
      if(step.state==='planned')assert.equal(step.destination,undefined,role+': '+step.id);
      if(step.destination){
        const tab=new URL(step.destination,'https://fixture.invalid').searchParams.get('tab');
        assert.equal(normalizeTab(tab,role),tab,role+': '+step.id);
      }
    }
  }
});

test('actual profile fields are required before a completed profile is shown',()=>{
  assert.equal(mobileGuide({role:'parent',first_name:'Anna',last_name:'Fixture',birthday:'2010-01-01'})[0].state,'verified');
  assert.equal(mobileGuide({role:'coach',first_name:'Anna',last_name:'',birthday:'2010-01-01'})[0].state,'ready');
  assert.equal(mobileGuide({role:'fan',first_name:' ',last_name:'Test',birthday:'2010-01-01'})[0].state,'ready');
  assert.equal(mobileGuide({role:'player',first_name:'Anna',last_name:'Test'})[0].state,'ready');
});

test('admin walkthrough is owner-scoped and never invents publication or checkout',()=>{
  const manager=adminGuide({role:'manager',name:'Manager',organizations:[{id:'lefteria_fc',name:'FC'}],permissions:['teams:*','shop:read']});
  assert.deepEqual(manager.map(s=>s.id),['account','organizations','groups','drafts','pos']);
  assert.equal(manager.find(s=>s.id==='drafts')?.destination,'/admin/announcements');
  const coach=adminGuide({role:'coach',name:'Coach',organizations:[{id:'lefteria_fc_academy',name:'Academy'}],permissions:['academy:read_assigned']});
  assert.ok(!coach.some(s=>s.id==='drafts'));
  assert.ok(coach.some(s=>s.state==='planned'));
  assert.equal(adminGuide({role:'editor',name:'Editor',organizations:[],permissions:['news:read']})
    .find(s=>s.id==='organizations')?.state,'ready');
});

test('mobile onboarding is integrated with real current-role profile, and web onboarding is separate',()=>{
  const app=source('../src/routes/app/[...path]/+page.svelte');
  const profile=source('../src/lib/mobile/components/MobileProfile.svelte');
  const guided=source('../src/lib/onboarding/GuidedSetup.svelte');
  const admin=source('../src/routes/admin/onboarding/+page.svelte');
  assert.match(app,/onNavigate=\{async href=>\{await goto\(href\);\}\}/);
  assert.match(app,/activeTab === 'profile' && !composing/);
  assert.doesNotMatch(app,/if \(role === 'fan' \|\| await controller\.switchRole\('fan'\)\)/);
  assert.match(profile,/\{#if user\.role === 'fan'\}/);
  assert.match(profile,/data-testid="mobile-own-profile"/);
  assert.match(guided,/localStorage\.setItem/);
  assert.match(guided,/step\.state!=='ready' \|\| id==='profile'/);
  assert.match(admin,/adminGuide\(\$auth\.session\.user\)/);
  assert.doesNotMatch(admin,/method:'POST'|fetch\(|\/api\/admin\/profile/);
});
