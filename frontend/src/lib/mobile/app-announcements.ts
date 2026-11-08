import {validReactions,type GroupChatRoom,type GroupChatReaction} from './group-chat.ts';

export type AppAnnouncementMedia={
  id:string;kind:'banner'|'attachment';file_name:string;
  content_type:'image/jpeg'|'application/pdf';size:number
};
export type AppAnnouncementMediaDownload=AppAnnouncementMedia &{base64:string};
export function validAppAnnouncementMedia(raw:unknown):raw is AppAnnouncementMedia {
  if(!value(raw))return false;
  return typeof raw.id==='string'&&/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(raw.id)&&
    typeof raw.file_name==='string'&&raw.file_name.length>0&&raw.file_name.length<=96&&
    Number.isInteger(raw.size)&&
    ((raw.kind==='banner'&&raw.content_type==='image/jpeg'&&
       raw.file_name==='banner.jpg'&&(raw.size as number)>=4&&(raw.size as number)<=1048576)||
     (raw.kind==='attachment'&&raw.content_type==='application/pdf'&&
       /^[A-Za-z0-9][A-Za-z0-9 ._()-]{0,91}\.pdf$/i.test(raw.file_name)&&
       (raw.size as number)>=8&&(raw.size as number)<=2097152));
}
export function validAppAnnouncementMediaList(raw:unknown):raw is AppAnnouncementMedia[] {
  return Array.isArray(raw)&&raw.length<=4&&
    raw.every(validAppAnnouncementMedia)&&
    raw.filter(m=>m.kind==='banner').length<=1&&
    raw.filter(m=>m.kind==='attachment').length<=3&&
    new Set(raw.map(m=>m.id)).size===raw.length;
}
export function validAppAnnouncementMediaDownload(raw:unknown,expected:AppAnnouncementMedia):
  raw is AppAnnouncementMediaDownload {
  if(!value(raw)||!validAppAnnouncementMedia(raw))return false;
  return raw.id===expected.id&&raw.kind===expected.kind&&
    raw.file_name===expected.file_name&&raw.content_type===expected.content_type&&
    raw.size===expected.size&&typeof raw.base64==='string'&&
    raw.base64.length<=Math.ceil(expected.size/3)*4+Math.ceil(expected.size/57)*2+8&&
    /^[A-Za-z0-9+/=\r\n]*$/.test(raw.base64);
}
export type AppAnnouncement={
  id:string;organization_id:'lefteria_fc'|'lefteria_fc_academy';
  group_id:string;title:string;body:string;published_at:string;status:'published';
  reactions:GroupChatReaction[];media?:AppAnnouncementMedia[]
};
export type AppAnnouncementPage={announcements:AppAnnouncement[];next_offset:number|null};

const value=(v:unknown):v is Record<string,unknown>=>
  !!v&&typeof v==='object'&&!Array.isArray(v);
export function validAppAnnouncement(raw:unknown,room:GroupChatRoom):raw is AppAnnouncement {
  return value(raw)&&typeof raw.id==='string'&&/^ann_[a-f0-9]{32}$/.test(raw.id)&&
    raw.organization_id===room.organization_id&&raw.group_id===room.group_id&&
    raw.status==='published'&&validReactions(raw.reactions)&&
    (raw.media===undefined||validAppAnnouncementMediaList(raw.media))&&
    typeof raw.title==='string'&&raw.title.length>0&&raw.title.length<=140&&
    typeof raw.body==='string'&&raw.body.length>0&&raw.body.length<=4000&&
    typeof raw.published_at==='string'&&raw.published_at.length<=64&&
    !Number.isNaN(Date.parse(raw.published_at));
}
export function validAppAnnouncementPage(raw:unknown,room:GroupChatRoom):raw is AppAnnouncementPage {
  if(!value(raw)||!Array.isArray(raw.announcements)||raw.announcements.length>50)return false;
  const items=raw.announcements;
  return items.every((item:unknown)=>validAppAnnouncement(item,room))&&
    new Set(items.map((a:AppAnnouncement)=>a.id)).size===items.length&&
    (raw.next_offset===null||(Number.isInteger(raw.next_offset)&&
      (raw.next_offset as number)>=0&&(raw.next_offset as number)<=100050));
}
export type AppAnnouncementReactionSnapshot={
  announcement_id:string;reactions:GroupChatReaction[]
};
export function validAppAnnouncementReactionSnapshot(raw:unknown,id:string):
  raw is AppAnnouncementReactionSnapshot {
  return value(raw)&&raw.announcement_id===id&&validReactions(raw.reactions);
}
