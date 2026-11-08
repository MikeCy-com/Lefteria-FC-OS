// Private app-only official announcement alerts. No device push API or
// third-party notification service is implied by this contract.
export type AppNotification={
  announcement_id:string;
  organization_id:'lefteria_fc'|'lefteria_fc_academy';
  group_id:string;group_name:string;title:string;
  published_at:string;seen:boolean;
};
export type AppNotificationInbox={
  notifications:AppNotification[];
  unread_count:number;
  next_offset:number|null;
  enabled:boolean;
};
const plain=(x:unknown):x is Record<string,unknown>=>
  !!x && typeof x==='object' && !Array.isArray(x);
export function validAppNotification(value:unknown):value is AppNotification {
  return plain(value)&&
    typeof value.announcement_id==='string'&&
    /^ann_[0-9a-f]{32}$/.test(value.announcement_id)&&
    (value.organization_id==='lefteria_fc'||value.organization_id==='lefteria_fc_academy')&&
    typeof value.group_id==='string'&&
    /^[A-Za-z0-9_-]{1,128}$/.test(value.group_id)&&
    typeof value.group_name==='string'&&value.group_name.length>0&&value.group_name.length<=200&&
    typeof value.title==='string'&&value.title.length>0&&value.title.length<=140&&
    typeof value.published_at==='string'&&value.published_at.length<=64&&
    !Number.isNaN(Date.parse(value.published_at))&&typeof value.seen==='boolean';
}
export function validAppNotificationInbox(value:unknown):value is AppNotificationInbox {
  if(!plain(value)||!Array.isArray(value.notifications)||
    value.notifications.length>30||!value.notifications.every(validAppNotification)||
    !Number.isInteger(value.unread_count)||(value.unread_count as number)<0||
    (value.unread_count as number)>999||typeof value.enabled!=='boolean')return false;
  const items=value.notifications as AppNotification[];
  if(new Set(items.map(item=>item.announcement_id)).size!==items.length)return false;
  if(!value.enabled&&(items.length!==0||value.unread_count!==0||value.next_offset!==null))
    return false;
  return value.next_offset===null||
    Number.isSafeInteger(value.next_offset)&&
    (value.next_offset as number)>=0&&(value.next_offset as number)<=100030;
}
