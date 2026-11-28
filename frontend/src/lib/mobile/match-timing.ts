export type MatchFormat=5|7|9|11;
export type MatchTiming={frozen:boolean;settings_version:number|null;duration_minutes:number|null;
  players_on_field:MatchFormat|null;attendance_ids:string[]|null};
export type GroupMatchSettings={organization_id:string;host_id:string;version:number;
  duration_minutes:number|null;players_on_field:MatchFormat|null};
const obj=(x:unknown):x is Record<string,unknown>=>!!x&&typeof x==='object'&&!Array.isArray(x);
const int=(x:unknown,min:number,max:number):x is number=>typeof x==='number'&&Number.isSafeInteger(x)&&x>=min&&x<=max;
export const validFormat=(x:unknown):x is MatchFormat=>x===5||x===7||x===9||x===11;
export function validGroupMatchSettings(raw:unknown,owner:string,host:string):raw is GroupMatchSettings{
  if(!obj(raw)||Object.keys(raw).sort().join(',')!=='duration_minutes,host_id,organization_id,players_on_field,version'||
     raw.organization_id!==owner||raw.host_id!==host||!int(raw.version,0,1000000)||
     !['lefteria_fc','lefteria_fc_academy'].includes(owner))return false;
  return raw.version===0?raw.duration_minutes===null&&raw.players_on_field===null:
    int(raw.duration_minutes,1,180)&&validFormat(raw.players_on_field)&&(owner!=='lefteria_fc'||raw.players_on_field===11);
}
export function validMatchTiming(raw:unknown,owner:string,selected:ReadonlySet<string>,stale:boolean):raw is MatchTiming{
  if(!obj(raw)||Object.keys(raw).sort().join(',')!=='attendance_ids,duration_minutes,frozen,players_on_field,settings_version'||
    typeof raw.frozen!=='boolean')return false;
  if(raw.settings_version===null)return raw.duration_minutes===null&&raw.players_on_field===null&&raw.attendance_ids===null;
  if(!int(raw.settings_version,1,1000000)||!int(raw.duration_minutes,1,180)||!validFormat(raw.players_on_field)||
    (owner==='lefteria_fc'&&raw.players_on_field!==11))return false;
  if(raw.attendance_ids===null)return true;
  return owner==='lefteria_fc_academy'&&raw.frozen&&!stale&&Array.isArray(raw.attendance_ids)&&
    raw.attendance_ids.length>=1&&raw.attendance_ids.length<=60&&new Set(raw.attendance_ids).size===raw.attendance_ids.length&&
    raw.attendance_ids.every(id=>typeof id==='string'&&selected.has(id));
}
function gcd(a:number,b:number):number{while(b){const next=a%b;a=b;b=next;}return a;}
export type RotationPeriod={start_seconds:number;end_seconds:number;on_field:string[];enter:string[];leave:string[]};
export type RotationPlan={periods:RotationPeriod[];target_seconds:number;short_handed:boolean;
  planned_seconds:Record<string,number>;min_interval_seconds:number;max_interval_seconds:number};
/**
 * Attendance order is explicit and starts with the preferred opening group.
 * A circular sequence of K slots, advanced K each period, balances playing
 * opportunities in N/gcd(N,K) periods. Round boundaries to whole seconds.
 * No generated interval or allocation is a record of actual playing time.
 */
export function planGrassrootsRotation(durationMinutes:number,format:MatchFormat,attendance:readonly string[]):RotationPlan{
  if(!int(durationMinutes,1,180)||!validFormat(format)||attendance.length<1||attendance.length>60||
    new Set(attendance).size!==attendance.length||attendance.some(id=>typeof id!=='string'||!id||id.length>128))
    throw Error('Invalid match rotation inputs');
  const n=attendance.length,k=Math.min(n,format),seconds=durationMinutes*60,cycle=n/gcd(n,k);
  // Repeat whole balanced cycles rather than leaving a child on the bench for
  // a half when the squad has a simple ratio. Ten minutes is a planner target,
  // not a group setting or a mandatory substitution rule.
  const blocks=n===k?1:cycle*Math.max(1,Math.ceil(seconds/(cycle*600)));
  const planned=new Map(attendance.map(id=>[id,0]));
  const periods:RotationPeriod[]=[];
  let previous:string[]=[];
  for(let b=0;b<blocks;b++){
    const start=Math.floor(b*seconds/blocks),end=Math.floor((b+1)*seconds/blocks);
    const onField=Array.from({length:k},(_,i)=>attendance[(b*k+i)%n]);
    for(const id of onField)planned.set(id,(planned.get(id)??0)+end-start);
    periods.push({start_seconds:start,end_seconds:end,on_field:onField,
      enter:b===0?onField:onField.filter(id=>!previous.includes(id)),leave:previous.filter(id=>!onField.includes(id))});
    previous=onField;
  }
  return {periods,target_seconds:seconds*k/n,short_handed:n<format,planned_seconds:Object.fromEntries(planned),
    min_interval_seconds:Math.floor(seconds/blocks),max_interval_seconds:Math.ceil(seconds/blocks)};
}
export function rotationClock(seconds:number):string{
  const value=Math.max(0,Math.round(seconds));return Math.floor(value/60)+':'+String(value%60).padStart(2,'0');
}
