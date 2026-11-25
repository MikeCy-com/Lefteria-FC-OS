import type {MobileCalendar,MobileCalendarItem} from './calendar.ts';
import type {ParentTrainingFees} from './finance.ts';

export function calendarActions(calendar:MobileCalendar,today:string){
  const upcoming=calendar.items.filter(item=>item.end_date>=today&&
    item.date<=today.slice(0,7)+'-31'&&
    (calendar.role==='coach'
      ?item.organization_id==='lefteria_fc'||item.organization_id==='lefteria_fc_academy'
      :item.organization_id===calendar.organization_id));
  const unique=new Map<string,MobileCalendarItem>();
  for(const item of upcoming)unique.set(item.kind+':'+item.id,item);
  const ordered=[...unique.values()].sort((a,b)=>a.date.localeCompare(b.date)||
    (a.start_time??'').localeCompare(b.start_time??'')||a.id.localeCompare(b.id));
  const cancelled=ordered.filter(item=>item.is_cancelled);
  const unanswered=(calendar.role==='parent'||calendar.role==='player')&&calendar.selected_player_id
    ?ordered.filter(item=>!item.is_cancelled&&(item.kind==='match'||item.kind==='training')&&
      !item.responses.some(r=>r.player_id===calendar.selected_player_id))
    :[];
  return {cancelled:cancelled.slice(0,3),unanswered:unanswered.slice(0,3),
    limited:calendar.next_offset!==null||cancelled.length>3||unanswered.length>3};
}

export function feeActions(snapshot:ParentTrainingFees){
  const seen=new Set<string>();
  let outstanding=0,review=0,settling=0;
  for(const fee of snapshot.fees){
    if(seen.has(fee.id))throw new Error('Duplicate fee in action snapshot.');
    seen.add(fee.id);
    // Review and settlement are not a request to pay again.
    if(fee.status==='review_required'||fee.payment_status==='review_required')review++;
    else if(fee.status==='paid')continue;
    else if(fee.payment_status==='awaiting_settlement'||fee.payment_status==='initiating'||fee.payment_status==='paid')settling++;
    else if(fee.status==='issued')outstanding++;
  }
  return {outstanding,review,settling};
}
