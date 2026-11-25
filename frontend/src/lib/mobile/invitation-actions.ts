import type {PrivateConversation} from './private-chat.ts';
import type {MobileRole} from './navigation.ts';

export function canOpenPrivateChat(role:MobileRole|null):boolean {
  return role==='parent'||role==='coach'||role==='management';
}
export function privateChatMode(role:MobileRole|null,requested:string|null):'groups'|'private' {
  return canOpenPrivateChat(role)&&requested==='private'?'private':'groups';
}

// A consent invitation is not an unread message. Count only incoming pending
// conversations from a validated server response, with explicit management scope.
export function pendingPrivateInvitations(rows:PrivateConversation[],role:MobileRole,
  organizationId:string|null):number {
  if(!canOpenPrivateChat(role))throw new Error('Private chat is unavailable.');
  if(role==='management'&&!organizationId)throw new Error('Organization required.');
  if(role==='management'&&rows.some(row=>row.organization_id!==organizationId))
    throw new Error('Unexpected invitation organization.');
  return rows.filter(row=>row.status==='pending'&&!row.is_initiator).length;
}

// Recipient endpoint intentionally exposes at most one live invitation and no
// youth identity. Return a count only, never retain upstream invitation details.
export function pendingFamilyInvitationCount(raw:unknown,now=Date.now()):number {
  if(!raw||typeof raw!=='object'||!('items' in raw)||!Array.isArray(raw.items)||raw.items.length>1)
    throw new Error('Invalid invitation response.');
  for(const row of raw.items){
    if(!row||typeof row!=='object'||typeof row.id!=='string'||
      !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(row.id)||
      (row.organization_id!=='lefteria_fc'&&row.organization_id!=='lefteria_fc_academy')||
      row.status!=='pending'||typeof row.created_at!=='string'||typeof row.expires_at!=='string'||
      !Number.isFinite(Date.parse(row.created_at))||!Number.isFinite(Date.parse(row.expires_at))||
      Date.parse(row.expires_at)<=Date.parse(row.created_at)||Date.parse(row.expires_at)<=now)
      throw new Error('Unverified invitation response.');
  }
  return raw.items.length;
}
