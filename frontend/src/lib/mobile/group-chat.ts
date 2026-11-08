// Validated server snapshots only; do not cache member messages in browser
// storage or claim delivery/read status from successful HTTP responses.
import {CHAT_REACTION_EMOJIS,type ChatReaction} from './chat-actions.ts';

export type GroupChatReaction={emoji:ChatReaction;count:number;mine:boolean};
export type GroupChatReactionSnapshot={message_id:string;reactions:GroupChatReaction[]};
export type GroupChatRoom = {organization_id:string;group_id:string;kind:'team'|'academy_group';name:string};
export type GroupChatMessage = {id:string;organization_id:string;group_id:string;
  sender_id:string;sender_name:string;content:string;created_at:string;
  edited_at:string|null;is_deleted:boolean;version:number;
  reactions:GroupChatReaction[]};
export type GroupChatPage = {messages:GroupChatMessage[];next_offset:number|null};

const value=(v:unknown):v is Record<string,unknown>=>!!v&&typeof v==='object'&&!Array.isArray(v);
const text=(v:unknown,max:number):v is string=>typeof v==='string'&&v.length>0&&v.length<=max;
export function validRoom(v:unknown):v is GroupChatRoom {
  if(!value(v))return false;
  return (v.organization_id==='lefteria_fc'&&v.kind==='team'||
    v.organization_id==='lefteria_fc_academy'&&v.kind==='academy_group')&&
    text(v.group_id,128)&&/^[a-zA-Z0-9_-]+$/.test(v.group_id)&&text(v.name,200);
}
export function validChatDirectory(v:unknown):v is {ready:true;groups:{id:string;organization_id:string;kind:'team'|'academy_group';name:string}[]} {
  if(!value(v)||v.ready!==true||!Array.isArray(v.groups)||v.groups.length>150)return false;
  const groups=v.groups;
  return groups.every((group:unknown)=>{
    if(!value(group))return false;
    return validRoom({organization_id:group.organization_id,group_id:group.id,kind:group.kind,name:group.name});
  })&&new Set(groups.map((r:{id:string;organization_id:string})=>r.organization_id+'/'+r.id)).size===groups.length;
}
export function validReactions(v:unknown):v is GroupChatReaction[] {
  if(!Array.isArray(v)||v.length>CHAT_REACTION_EMOJIS.length)return false;
  const used=new Set<string>();
  return v.every((r:unknown)=>{
    if(!value(r)||typeof r.emoji!=='string'||
       !CHAT_REACTION_EMOJIS.some(emoji=>emoji===r.emoji)||
       !Number.isSafeInteger(r.count)||(r.count as number)<1||
       typeof r.mine!=='boolean'||used.has(r.emoji))return false;
    used.add(r.emoji);
    return true;
  });
}
export function validReactionSnapshot(v:unknown,messageId:string):v is GroupChatReactionSnapshot {
  return value(v)&&v.message_id===messageId&&validReactions(v.reactions);
}

export function validMessage(v:unknown,room:GroupChatRoom):v is GroupChatMessage {
  return value(v)&&typeof v.id==='string'&&/^[a-f0-9-]{36}$/i.test(v.id)&&
    v.organization_id===room.organization_id&&v.group_id===room.group_id&&
    text(v.sender_id,36)&&text(v.sender_name,200)&&
    typeof v.is_deleted==='boolean'&&Number.isSafeInteger(v.version)&&(v.version as number)>=1&&
    (v.edited_at===null||(text(v.edited_at,64)&&!Number.isNaN(Date.parse(v.edited_at))))&&
    (v.is_deleted?v.content==='':text(v.content,10000))&&
    validReactions(v.reactions)&&(!v.is_deleted||(v.reactions as unknown[]).length===0)&&
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
