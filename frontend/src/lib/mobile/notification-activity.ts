export type ActivityKind='announcement'|'group_message'|'private_message';
export type ActivityFilter='all'|ActivityKind;
export type NotificationActivity={
  kind:ActivityKind;entity_id:string;organization_id:'lefteria_fc'|'lefteria_fc_academy';
  group_id:string;conversation_id:string|null;occurred_at:string;title:string|null;group_name:string|null;
};
export type NotificationActivityPage={items:NotificationActivity[];next_offset:number|null};
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const plain=(x:unknown):x is Record<string,unknown>=>!!x&&typeof x==='object'&&!Array.isArray(x);
export function validActivityFilter(x:unknown):x is ActivityFilter {
  return x==='all'||x==='announcement'||x==='group_message'||x==='private_message';
}
export function validActivityPage(raw:unknown,filter:ActivityFilter,offset:number,
  privateAllowed:boolean,organization?:string|null):raw is NotificationActivityPage {
  if(!plain(raw)||Object.keys(raw).sort().join(',')!=='items,next_offset'||
    !Array.isArray(raw.items)||raw.items.length>30||
    !(raw.next_offset===null||(raw.items.length===30&&raw.next_offset===offset+30&&offset+30<=100000)))return false;
  const seen=new Set<string>();let previous=Infinity;
  return raw.items.every(row=>{
    if(!plain(row)||Object.keys(row).sort().join(',')!=='conversation_id,entity_id,group_id,group_name,kind,occurred_at,organization_id,title'||
      !validActivityFilter(row.kind)||row.kind==='all'||(filter!=='all'&&row.kind!==filter)||
      (row.organization_id!=='lefteria_fc'&&row.organization_id!=='lefteria_fc_academy')||
      (organization&&row.organization_id!==organization)||
      typeof row.group_id!=='string'||!/^[A-Za-z0-9_-]{1,128}$/.test(row.group_id)||
      typeof row.entity_id!=='string'||typeof row.occurred_at!=='string'||row.occurred_at.length>64)return false;
    const time=Date.parse(row.occurred_at),key=row.kind+':'+row.entity_id;
    if(!Number.isFinite(time)||time>previous||seen.has(key))return false;
    previous=time;seen.add(key);
    const group=typeof row.group_name==='string'&&row.group_name.length>0&&row.group_name.length<=200;
    if(row.kind==='announcement')return /^ann_[0-9a-f]{32}$/.test(row.entity_id)&&
      row.conversation_id===null&&group&&typeof row.title==='string'&&row.title.length>0&&row.title.length<=140;
    if(!uuid.test(row.entity_id)||row.title!==null)return false;
    if(row.kind==='group_message')return row.conversation_id===null&&group;
    return privateAllowed&&row.group_name===null&&typeof row.conversation_id==='string'&&uuid.test(row.conversation_id);
  });
}
export function activityDestination(item:NotificationActivity):string {
  const query=new URLSearchParams();
  if(item.kind==='announcement'){
    query.set('tab','announcements');query.set('organization_id',item.organization_id);
    query.set('group',item.group_id);query.set('notice',item.entity_id);
  }else if(item.kind==='group_message'){
    query.set('tab','chat');query.set('chat_mode','groups');
    query.set('organization_id',item.organization_id);query.set('group',item.group_id);
  }else{
    query.set('tab','chat');query.set('chat_mode','private');
    if(item.conversation_id)query.set('conversation',item.conversation_id);
  }
  // Link to the current authorized conversation, not a promise that an
  // older message is in its first page. Opening the link is not a receipt.
  return '/app?'+query.toString();
}
