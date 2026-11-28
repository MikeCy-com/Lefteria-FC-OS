import type {WebAdminSession} from './controller.ts';
import {hasAdminPermission} from './permissions.ts';

type Owner='lefteria_fc'|'lefteria_fc_academy';
type User=Pick<WebAdminSession['user'],'role'|'permissions'|'organizations'>;

// A single UI access policy for group workspaces. This is not authorization:
// every private read and mutation still checks the exact server-side grant.
export function groupWorkspaceAccess(user:User,owner:Owner,canManageGroup:boolean,attendanceScope:boolean){
  const has=(permission:string)=>hasAdminPermission(user.permissions,permission);
  const granted=user.organizations.some(org=>org.id===owner);
  const manager=granted&&['manager','super_admin'].includes(user.role);
  const academy=owner==='lefteria_fc_academy';
  const canEdit=manager&&canManageGroup&&has(academy?'academy:write':'teams:write');
  const training=manager&&academy&&has('training:read');
  const attendance=manager&&academy&&attendanceScope&&has('attendance:read');
  const matches=manager&&has('fixtures:read');
  return {
    players:canEdit,
    profileWrite:canEdit,
    training,trainingWrite:training&&has('training:write'),
    attendance,attendanceWrite:attendance&&has('attendance:write'),
    matches,matchWrite:matches&&canEdit&&has('fixtures:write'),
    coaches:canEdit,settings:canEdit,announcements:canEdit&&has('messages:write'),
    finance:canEdit&&academy&&user.role==='super_admin'&&has('finance:read')
  };
}
