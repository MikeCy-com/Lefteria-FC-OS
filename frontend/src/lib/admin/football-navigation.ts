import type {WebAdminSession} from './controller.ts';
import {hasAdminPermission} from './permissions.ts';

// Presentation only: the roster endpoint verifies exact team, city and location
// grants on every request. An organization grant alone does not grant any group.
export function footballNavigationFor(user:WebAdminSession['user']){
  const has=(p:string)=>hasAdminPermission(user.permissions,p);
  const first=user.organizations.some(org=>org.id==='lefteria_fc');
  const academy=user.organizations.some(org=>org.id==='lefteria_fc_academy');
  const manager=['super_admin','manager'].includes(user.role);
  return {
    firstTeamGroups:first&&manager&&has('teams:read'),
    academyGroups:academy&&manager&&has('academy:read'),
    academyCoachTools:academy&&user.role==='coach'&&has('training:read_assigned'),
    academyDrills:academy&&['super_admin','manager','coach'].includes(user.role)&&(has('training:read')||has('training:read_assigned'))
  };
}
