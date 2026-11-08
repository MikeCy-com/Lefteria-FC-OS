// App-only, adult-role roster-scoped private conversations. Never put these
// snapshots in localStorage/sessionStorage or treat guessed identifiers as grants.
export type PrivateContact={id:string;name:string;role:'parent'|'coach'|'management'};
export type PrivateConversation={
  id:string;organization_id:'lefteria_fc'|'lefteria_fc_academy';group_id:string;
  partner_id:string;partner_name:string;partner_role:PrivateContact['role'];
  status:'pending'|'accepted';is_initiator:boolean
};
export type PrivateMessage={
  id:string;conversation_id:string;sender_id:string;sender_name:string;
  content:string;created_at:string;edited_at:string|null;
  is_deleted:boolean;version:number
};
export type PrivatePage={messages:PrivateMessage[];next_offset:number|null};

const value=(v:unknown):v is Record<string,unknown>=>
  typeof v==='object'&&v!==null&&!Array.isArray(v);
export const validUuid=(v:unknown):v is string=>
  typeof v==='string'&&/^[a-f0-9]{8}-[a-f0-9]{4}-[1-8][a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/i.test(v);
const name=(v:unknown):v is string=>
  typeof v==='string'&&v.length>0&&v.length<=200;
const role=(v:unknown):v is PrivateContact['role']=>
  v==='parent'||v==='coach'||v==='management';
const timestamp=(v:unknown):v is string=>
  typeof v==='string'&&v.length<=64&&v.length>0&&!Number.isNaN(Date.parse(v));
export const validPrivateContact=(v:unknown):v is PrivateContact=>
  value(v)&&validUuid(v.id)&&name(v.name)&&role(v.role);
export function validPrivateContacts(v:unknown):v is {contacts:PrivateContact[]} {
  if(!value(v)||!Array.isArray(v.contacts)||v.contacts.length>100)return false;
  return v.contacts.every(validPrivateContact)&&
    new Set(v.contacts.map((c:PrivateContact)=>c.id)).size===v.contacts.length;
}
export function validPrivateConversation(v:unknown):v is PrivateConversation {
  return value(v)&&validUuid(v.id)&&validUuid(v.partner_id)&&name(v.partner_name)&&
    role(v.partner_role)&&(v.organization_id==='lefteria_fc'||
    v.organization_id==='lefteria_fc_academy')&&
    typeof v.group_id==='string'&&/^[a-zA-Z0-9_-]{1,128}$/.test(v.group_id)&&
    (v.status==='pending'||v.status==='accepted')&&typeof v.is_initiator==='boolean';
}
export function validPrivateInbox(v:unknown):v is {conversations:PrivateConversation[]} {
  if(!value(v)||!Array.isArray(v.conversations)||v.conversations.length>100)return false;
  return v.conversations.every(validPrivateConversation)&&
    new Set(v.conversations.map((c:PrivateConversation)=>c.id)).size===v.conversations.length;
}
export function validPrivateMessage(v:unknown,id:string):v is PrivateMessage {
  return value(v)&&validUuid(v.id)&&v.conversation_id===id&&
    validUuid(v.sender_id)&&name(v.sender_name)&&
    timestamp(v.created_at)&&(v.edited_at===null||timestamp(v.edited_at))&&
    typeof v.is_deleted==='boolean'&&Number.isSafeInteger(v.version)&&
    (v.version as number)>=1&&
    (v.is_deleted?v.content==='':typeof v.content==='string'&&
      v.content.length>0&&v.content.length<=10000&&v.content===v.content.trim()&&
      !/[\u0000-\u001f\u007f]/.test(v.content));
}
export function validPrivatePage(v:unknown,id:string):v is PrivatePage {
  if(!value(v)||!Array.isArray(v.messages)||v.messages.length>50)return false;
  return v.messages.every((m:unknown)=>validPrivateMessage(m,id))&&
    (v.next_offset===null||(Number.isInteger(v.next_offset)&&
      (v.next_offset as number)>=0&&(v.next_offset as number)<=100050));
}
export function validPrivateText(text:string):boolean {
  return text.length>0&&text.length<=10000&&text===text.trim()&&
    !/[\u0000-\u001f\u007f]/.test(text);
}
