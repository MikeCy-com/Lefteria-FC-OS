import type {GroupChatRoom} from './group-chat.ts';

export type AppAnnouncement={
  id:string;organization_id:'lefteria_fc'|'lefteria_fc_academy';
  group_id:string;title:string;body:string;published_at:string;status:'published'
};
export type AppAnnouncementPage={announcements:AppAnnouncement[];next_offset:number|null};

const value=(v:unknown):v is Record<string,unknown>=>
  !!v&&typeof v==='object'&&!Array.isArray(v);
export function validAppAnnouncement(raw:unknown,room:GroupChatRoom):raw is AppAnnouncement {
  return value(raw)&&typeof raw.id==='string'&&/^ann_[a-f0-9]{32}$/.test(raw.id)&&
    raw.organization_id===room.organization_id&&raw.group_id===room.group_id&&
    raw.status==='published'&&
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
