import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {adminRoleActions,adminRoleIntro} from '../src/lib/admin/admin-role-workflows.ts';
import {adminSectionsFor} from '../src/lib/admin/module-catalog.ts';

const read=path=>readFileSync(new URL('../src/lib/admin/'+path,import.meta.url),'utf8');
const user=(role,permissions,ids=['lefteria_fc_academy'])=>({
  id:'test',username:'test',name:'Staff',role,permissions,
  organizations:ids.map(id=>({id,name:id}))
});
const ids=account=>adminRoleActions(account).map(a=>a.id);
const urls=account=>adminRoleActions(account).map(a=>a.href);

test('role workflow cards are a strict subset of authorized live modules',()=>{
  const fixtures=[
    user('super_admin',['*:*'],['lefteria_fc','lefteria_fc_academy']),
    user('manager',['academy:read','academy:write','marketing:read']),
    user('manager',['academy:read']),
    user('accountant',['finance:read']),
    user('cashier',['shop:operate','shop:read']),
    user('editor',['news:read','gallery:read']),
    user('marketing',['marketing:read']),
    user('coach',['academy:read_assigned']),
    user('matchday_editor',['fixtures:read']),
    user('super_admin',['*:*'],[])
  ];
  for(const account of fixtures){
    const live=new Set(adminSectionsFor(account).flatMap(s=>s.modules)
      .filter(m=>m.status==='live'&&m.href).map(m=>m.id));
    const actions=adminRoleActions(account);
    assert.ok(actions.length<=6);
    assert.equal(new Set(actions.map(a=>a.href)).size,actions.length);
    for(const a of actions){
      assert.ok(live.has(a.id),'unpermitted card '+a.id);
      assert.match(a.href,/^\/admin\//);
      assert.ok(a.title.length>0&&a.description.length>0);
    }
    assert.ok(adminRoleIntro(account.role).length>10);
  }
});

test('role-specific work queues never escalate finance or youth access',()=>{
  const accountant=user('accountant',['finance:read']);
  assert.deepEqual(ids(accountant),['payabl']);
  assert.deepEqual(urls(accountant),['/admin/finance']);
  const cashier=user('cashier',['shop:operate','shop:read']);
  assert.deepEqual(ids(cashier),['registers']);
  assert.deepEqual(ids(user('coach',['academy:read_assigned'])),[]);
  assert.deepEqual(ids(user('marketing',['marketing:read'])),[]);
  assert.deepEqual(ids(user('super_admin',['*:*'],[])),[]);
  assert.ok(!urls(user('manager',['academy:read','academy:write','finance:read']))
    .includes('/admin/finance'));
  assert.ok(!ids(user('editor',['finance:read','shop:operate'])).includes('payabl'));
  assert.ok(!ids(user('cashier',['shop:operate'])).includes('registrations'));
});

test('only Academy write-authorized managers get player-focused CRM deep link',()=>{
  const writable=user('manager',['academy:read','academy:write']);
  assert.ok(urls(writable).includes('/admin/groups?organization_id=lefteria_fc_academy&view=players'));
  assert.ok(urls(writable).includes('/admin/academy-office?queue=action'));
  const readonly=user('manager',['academy:read']);
  assert.ok(urls(readonly).includes('/admin/groups'));
  assert.ok(!urls(readonly).some(url=>url.includes('view=players')));
  const firstTeam=user('manager',['teams:read','teams:write'],['lefteria_fc']);
  assert.ok(!urls(firstTeam).some(url=>url.includes('academy-office')||url.includes('view=players')));
});

test('new dashboard and role workboard are Svelte compilable with no invented statistics',()=>{
  for(const [filename,c] of [
    ['AdminRoleWorkboard.svelte',read('AdminRoleWorkboard.svelte')],
    ['AdminWorkspace.svelte',read('AdminWorkspace.svelte')]
  ]){
    assert.ok(compile(c,{filename,generate:'server'}).js.code.length>50);
    assert.doesNotMatch(c,/fetch\(|\bPOST\b|\bPUT\b|\bDELETE\b/);
  }
  const workboard=read('AdminRoleWorkboard.svelte');
  assert.match(workboard,/data-testid="admin-role-workboard"/);
  assert.match(workboard,/data-testid="admin-role-no-assigned-actions"/);
  assert.match(workboard,/href="\/admin\/profile#access"/);
  assert.match(workboard,/adminRoleActions\(user\)/);
});
