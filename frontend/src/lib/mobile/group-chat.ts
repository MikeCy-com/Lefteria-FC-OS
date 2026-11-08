// Validated server snapshots only; do not cache member messages in browser
// storage or claim delivery/read status from successful HTTP responses.
export type GroupChatRoom = {organization_id:string;group_id:string;kind:'team'|'academy_group';name:string};
export type GroupChatMessage = {id:string;organization_id:string;group_id:string;
  sender_id:string;sender_name:string;content:string;created_at:string};
export type GroupChatPage = {messages:GroupChatMessage[];next_offset:number|null};

const value=(v:unknown):v is Record<string,unknown>=>!!v&&typeof v==='object'&&!Array.isArray(v);
const text=(v:unknown,max:number):v is string=>typeof v==='string'&&v.length>0&&v.length<=max;
export function validRoom(v:unknown):v is GroupChatRoom {
  if(!value(v))return false;
  return (v.organization_id==='lefteria_fc'&&v.kind==='team'||
    v.organization_id==='lefteria_fc_academy'&&v.kind==='academy_group')&&
    text(v.group_id,128)&&/^[a-zA-Z0-9_-]+$/.test(v.group_id)&&text(v.name,200);
}
export function validRooms(v:unknown):v is {rooms:GroupChatRoom[]} {
  return value(v)&&Array.isArray(v.rooms)&&v.rooms.length<=200&&v.rooms.every(validRoom)&&
    new Set(v.rooms.map((r:GroupChatRoom)=>r.organization_id+'/'+r.group_id)).size===v.rooms.length;
}
export function validMessage(v:unknown,room:GroupChatRoom):v is GroupChatMessage {
  return value(v)&&typeof v.id==='string'&&/^[a-f0-9-]{36}$/i.test(v.id)&&
    v.organization_id===room.organization_id&&v.group_id===room.group_id&&
    text(v.sender_id,36)&&text(v.sender_name,200)&&text(v.content,10000)&&
    text(v.created_at,64)&&!Number.isNaN(Date.parse(v.created_at));
}
export function validPage(v:unknown,room:GroupChatRoom):v is GroupChatPage {
  return value(v)&&Array.isArray(v.messages)&&v.messages.length<=50&&
    v.messages.every((m:unknown)=>validMessage(m,room))&&
    (v.next_offset===null||(Number.isInteger(v.next_offset)&&
      (v.next_offset as number)>=0&&(v.next_offset as number)<=100050));
}
export function validTextMessage(content:string):boolean {
  return content.length>0&&content.length<=10000&&content===content.trim()&&
    !/[\u0000-\u001f\u007f]/.test(content);
}
