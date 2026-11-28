import test from 'node:test';
import assert from 'node:assert/strict';
import {footballNavigationFor} from '../src/lib/admin/football-navigation.ts';

const first={id:'lefteria_fc',name:'First Team'};
const academy={id:'lefteria_fc_academy',name:'Academy'};
const user=(role,organizations,permissions)=>({role,organizations,permissions});
test('super admin sees only granted football organizations',()=>{
  assert.deepEqual(footballNavigationFor(user('super_admin',[first,academy],['*:*'])),{
    firstTeamGroups:true,academyGroups:true,academyCoachTools:false,academyDrills:true
  });
  assert.equal(footballNavigationFor(user('super_admin',[academy],['*:*'])).firstTeamGroups,false);
  assert.equal(footballNavigationFor(user('super_admin',[first],['*:*'])).academyGroups,false);
});
test('delegated managers see only granted and permitted organization menus',()=>{
  const permission=['teams:read','academy:read','training:read'];
  assert.equal(footballNavigationFor(user('manager',[academy],permission)).firstTeamGroups,false);
  assert.equal(footballNavigationFor(user('manager',[academy],permission)).academyGroups,true);
  assert.equal(footballNavigationFor(user('manager',[first],permission)).academyGroups,false);
  assert.equal(footballNavigationFor(user('manager',[first,academy],['teams:read'])).academyGroups,false);
  assert.equal(footballNavigationFor(user('manager',[],permission)).academyGroups,false);
});
test('coach receives assigned academy tools, not private administrative roster',()=>{
  assert.deepEqual(footballNavigationFor(user('coach',[academy],['academy:read_assigned','training:read_assigned'])),{
    firstTeamGroups:false,academyGroups:false,academyCoachTools:true,academyDrills:true
  });
  assert.equal(footballNavigationFor(user('coach',[first],['training:read_assigned'])).academyCoachTools,false);
});
test('finance, marketing and editorial roles do not inherit roster access from an organization grant',()=>{
  for(const role of ['accountant','cashier','editor','marketing','matchday_editor']){
    const value=footballNavigationFor(user(role,[first,academy],['teams:read','academy:read','training:read']));
    assert.deepEqual(value,{firstTeamGroups:false,academyGroups:false,academyCoachTools:false,academyDrills:false});
  }
});
