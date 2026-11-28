export type MatchQueueRow={opponent_id:string;playing_field_id:string;match_date:string;kickoff_local:string;competition:string;home_game:boolean};
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
export function validMatchQueueRow(row:MatchQueueRow):boolean{
  return uuid.test(row.opponent_id)&&uuid.test(row.playing_field_id)&&
    /^\d{4}-\d{2}-\d{2}$/.test(row.match_date)&&Number.isFinite(Date.parse(row.match_date))&&
    new Date(row.match_date).toISOString().slice(0,10)===row.match_date&&
    /^([01]\d|2[0-3]):[0-5]\d$/.test(row.kickoff_local)&&
    row.competition.length<=160&&!/[\x00-\x1f\x7f]/.test(row.competition)&&typeof row.home_game==='boolean';
}
export function matchQueueConflict(row:Pick<MatchQueueRow,'match_date'|'kickoff_local'>,
  existing:readonly Pick<MatchQueueRow,'match_date'|'kickoff_local'>[]):boolean{
  return existing.some(d=>d.match_date===row.match_date&&d.kickoff_local.slice(0,5)===row.kickoff_local.slice(0,5));
}
