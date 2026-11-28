import {projectPotm} from '../public/potm.ts';
import type {PublicSections} from '../public/sections-types';
import {clubToday} from './calendar.ts';

export type PotmBallot={month_key:string;voting_open:boolean;my_vote_player_id:string|null;revision:number};
export type PotmBallotCandidate={id:string;name:string;number:number;position:string|null;imageUrl:string|null};

export function validPotmPlayerId(value:unknown):value is string {
  return typeof value==='string'&&value.length>=1&&value.length<=128&&value===value.trim()&&!/[\x00-\x1f\x7f-\x9f]/.test(value);
}
export function validPotmMonth(value:unknown):value is string {
  return typeof value==='string'&&/^(?!0000)[0-9]{4}-(0[1-9]|1[0-2])$/.test(value);
}
export function validPotmBallot(raw:unknown):raw is PotmBallot {
  if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
  const row=raw as Record<string,unknown>;
  return Object.keys(row).sort().join(',')==='month_key,my_vote_player_id,revision,voting_open'&&
    validPotmMonth(row.month_key)&&typeof row.voting_open==='boolean'&&
    typeof row.revision==='number'&&Number.isSafeInteger(row.revision)&&row.revision>=0&&row.revision<=2147483647&&
    (row.my_vote_player_id===null||(validPotmPlayerId(row.my_vote_player_id)&&row.revision>0));
}

export function validPotmMutationResult(previous:PotmBallot,playerId:string|null,raw:unknown):raw is PotmBallot {
  return validPotmBallot(raw)&&raw.voting_open&&raw.month_key===previous.month_key&&
    raw.my_vote_player_id===playerId&&raw.revision===previous.revision+(playerId===previous.my_vote_player_id?0:1);
}

/** Candidate names and images come only from the public projection, never from
 * a private roster response. The canonical picker contributes eligible IDs
 * only. The final SQL write still validates publication, identity and activity. */
export function potmCandidateOptions(publicRaw:unknown,canonicalRaw:unknown,month:string):PotmBallotCandidate[] {
  if(!validPotmMonth(month)||!publicRaw||typeof publicRaw!=='object'||Array.isArray(publicRaw)||
     !canonicalRaw||typeof canonicalRaw!=='object'||Array.isArray(canonicalRaw)||
     !('players' in canonicalRaw)||!Array.isArray(canonicalRaw.players)||canonicalRaw.players.length>500)
    throw new Error('Unverified POTM candidates.');
  const canonicalIds=new Set<string>();
  for(const entry of canonicalRaw.players) {
    if(!entry||typeof entry!=='object'||!validPotmPlayerId(entry.id)||canonicalIds.has(entry.id))
      throw new Error('Unverified canonical roster.');
    canonicalIds.add(entry.id);
  }
  const view=projectPotm(publicRaw as PublicSections);
  if(view.status!=='not-published'||view.monthKey!==month||!view.snapshotAt||
     clubToday('Asia/Nicosia',Date.parse(view.snapshotAt)).slice(0,7)!==month||
     view.candidates.length>500||view.candidates.some(p=>!validPotmPlayerId(p.id)))
    throw new Error('Unverified current public roster.');
  return view.candidates.filter(p=>canonicalIds.has(p.id))
    .map(({id,name,number,position,imageUrl})=>({id,name,number,position,imageUrl}));
}
