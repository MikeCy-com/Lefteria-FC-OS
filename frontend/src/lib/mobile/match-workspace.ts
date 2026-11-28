export type Assignment='starter'|'reserve';
export type MatchMode='first_team'|'grassroots';
export type MatchState='draft'|'selected'|'submitted'|'approved'|'rejected';
export type MatchAction='save_squad'|'select_squad'|'submit_statistics'|'approve'|'reject';
export type EligiblePlayer={player_id:string;name:string;number:number;position:string|null;
  last_assignment:Assignment|null;starts:number;reserves:number};
export type SquadPlayer={player_id:string;assignment:Assignment;position:string|null};
export type MatchStatistic={player_id:string;minutes_played:number;goals:number;assists:number};
export type MatchWorkspace={match_id:string;organization_id:string;host_id:string;mode:MatchMode;
  version:number;state:MatchState;completed:boolean;roster_stale:boolean;roster:EligiblePlayer[];
  squad:SquadPlayer[];statistics:MatchStatistic[];rotation_note:string;review_reason:string};
export type MatchCommand={action:MatchAction;expected_version:number;squad:SquadPlayer[]|null;
  statistics:MatchStatistic[]|null;rotation_note:string|null;review_reason:string|null};
const plain=(x:unknown):x is Record<string,unknown>=>!!x&&typeof x==='object'&&!Array.isArray(x);
const integer=(x:unknown,min:number,max:number):x is number=>typeof x==='number'&&Number.isSafeInteger(x)&&x>=min&&x<=max;
const identifier=(x:unknown):x is string=>typeof x==='string'&&x.length>0&&x.length<=128&&x===x.trim()&&!/[\x00-\x1f\x7f]/.test(x);
const text=(x:unknown,max:number):x is string=>typeof x==='string'&&x.length<=max&&!/[\x00-\x1f\x7f]/.test(x);
const assignment=(x:unknown):x is Assignment=>x==='starter'||x==='reserve';
function exact(x:Record<string,unknown>,keys:string){return Object.keys(x).sort().join(',')===keys;}
export function validMatchWorkspace(raw:unknown,match:string,owner:string):raw is MatchWorkspace{
  if(!plain(raw)||!exact(raw,'completed,host_id,match_id,mode,organization_id,review_reason,roster,roster_stale,rotation_note,squad,state,statistics,version')||
    raw.match_id!==match||raw.organization_id!==owner||!identifier(raw.host_id)||
    (owner!=='lefteria_fc'&&owner!=='lefteria_fc_academy')||
    raw.mode!==(owner==='lefteria_fc'?'first_team':'grassroots')||
    !integer(raw.version,0,1000000)||!['draft','selected','submitted','approved','rejected'].includes(String(raw.state))||
    typeof raw.completed!=='boolean'||typeof raw.roster_stale!=='boolean'||
    !text(raw.rotation_note,500)||!text(raw.review_reason,500)||
    !Array.isArray(raw.roster)||raw.roster.length>200||!Array.isArray(raw.squad)||raw.squad.length>60||
    !Array.isArray(raw.statistics)||raw.statistics.length>60)return false;
  const roster=new Set<string>();
  if(!raw.roster.every(r=>{
    if(!plain(r)||!exact(r,'last_assignment,name,number,player_id,position,reserves,starts')||
      !identifier(r.player_id)||roster.has(r.player_id)||!text(r.name,200)||!r.name||
      !integer(r.number,-1,100000)||!(r.position===null||text(r.position,100))||
      (raw.mode==='grassroots'&&r.position!==null)||
      !(r.last_assignment===null||assignment(r.last_assignment))||
      !integer(r.starts,0,1000000)||!integer(r.reserves,0,1000000))return false;
    roster.add(r.player_id);return true;
  }))return false;
  const selected=new Set<string>();
  let starters=0;
  if(!raw.squad.every(r=>{
    if(!plain(r)||!exact(r,'assignment,player_id,position')||!identifier(r.player_id)||
      !roster.has(r.player_id)||selected.has(r.player_id)||!assignment(r.assignment)||
      !(r.position===null||text(r.position,40))||(raw.mode==='grassroots'&&r.position!==null))return false;
    selected.add(r.player_id);if(r.assignment==='starter')starters++;return true;
  })||starters>11)return false;
  const statistics=new Set<string>();
  if(!raw.statistics.every(r=>{
    if(!plain(r)||!exact(r,'assists,goals,minutes_played,player_id')||!identifier(r.player_id)||
      !selected.has(r.player_id)||statistics.has(r.player_id)||
      !integer(r.minutes_played,0,180)||!integer(r.goals,0,50)||!integer(r.assists,0,50)||
      (r.minutes_played===0&&(r.goals>0||r.assists>0)))return false;
    statistics.add(r.player_id);return true;
  }))return false;
  if(raw.roster_stale)return selected.size===0&&statistics.size===0;
  if(raw.version===0)return raw.state==='draft'&&selected.size===0&&statistics.size===0;
  if(raw.state==='draft')return statistics.size===0;
  if(starters<1||(raw.mode==='first_team'&&starters!==11))return false;
  if(raw.state==='selected')return statistics.size===0;
  return raw.completed&&statistics.size===selected.size;
}

// Only the coach's selected match participants are considered. No RSVP,
// roster position or surname is treated as a permanent starting entitlement.
// Prior reserve -> fewer starts -> more reserve appearances -> stable ID.
export function recommendGrassrootsStarters(roster:readonly EligiblePlayer[],selected:readonly SquadPlayer[],count:number):SquadPlayer[]{
  if(!integer(count,1,11)||count>selected.length)throw Error('Invalid starting group size');
  const ids=new Set(selected.map(p=>p.player_id));
  if(ids.size!==selected.length)throw Error('Duplicate squad player');
  const candidates=roster.filter(p=>ids.has(p.player_id));
  if(candidates.length!==ids.size)throw Error('Unverified squad player');
  const ranked=[...candidates].sort((a,b)=>
    Number(b.last_assignment==='reserve')-Number(a.last_assignment==='reserve')||
    a.starts-b.starts||b.reserves-a.reserves||a.player_id.localeCompare(b.player_id,'en'));
  const starting=new Set(ranked.slice(0,count).map(p=>p.player_id));
  return selected.map(p=>({player_id:p.player_id,assignment:starting.has(p.player_id)?'starter':'reserve',position:null}));
}
export function repeatedGrassrootsReserve(roster:readonly EligiblePlayer[],squad:readonly SquadPlayer[]):boolean{
  const prior=new Map(roster.map(p=>[p.player_id,p.last_assignment]));
  return squad.some(p=>p.assignment==='reserve'&&prior.get(p.player_id)==='reserve')&&
    squad.some(p=>p.assignment==='starter'&&prior.get(p.player_id)==='starter');
}
export function statisticFromInputs(playerId:string,minutes:string,goals:string,assists:string):MatchStatistic|null{
  if(!identifier(playerId)||!/^\d{1,3}$/.test(minutes)||!/^\d{1,2}$/.test(goals)||!/^\d{1,2}$/.test(assists))return null;
  const m=Number(minutes),g=Number(goals),a=Number(assists);
  if(!integer(m,0,180)||!integer(g,0,50)||!integer(a,0,50)||(m===0&&(g!==0||a!==0)))return null;
  return {player_id:playerId,minutes_played:m,goals:g,assists:a};
}
