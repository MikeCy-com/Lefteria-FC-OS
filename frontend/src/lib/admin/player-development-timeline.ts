/**
 * Pure view projection of already-authorized private player records.
 * Never fetches data or widens access. Every item links to the existing
 * assessment/goal card; no second roster, event store or cached profile.
 */
export type TimelineAssessment = {
  id: string;
  summary: string|null;
  created_at: string;
};
export type TimelineGoal = {
  id: string;
  title: string;
  history_count: number;
  history: readonly {
    version: number;
    event: string;
    progress: number;
    note: string;
    recorded_at: string;
  }[];
};
export type DevelopmentTimelineEvent = {
  key: string;
  at: string;
  epoch: number;
  kind: 'assessment'|'goal';
  title: string;
  description: string;
  targetId: string;
};
export type DevelopmentTimelinePage = {
  events: DevelopmentTimelineEvent[];
  loadedCount: number;
  hiddenOlderCount: number;
  unreturnedGoalEvents: number;
};

const eventLabels: Record<string,string> = {
  created: 'Δημιουργία στόχου',
  progress: 'Αλλαγή προόδου',
  completed: 'Ολοκλήρωση στόχου',
  reopened: 'Επανέναρξη στόχου',
  archived: 'Αρχειοθέτηση στόχου'
};
function epoch(value:string):number|null {
  // Reject invalid/ambiguous local dates rather than invent an event time.
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/.test(value)) return null;
  const parsed=Date.parse(value);
  return Number.isFinite(parsed)?parsed:null;
}
export function buildPlayerDevelopmentTimeline(
  assessments: readonly TimelineAssessment[],
  goals: readonly TimelineGoal[],
  limit=40
):DevelopmentTimelinePage {
  const rows:DevelopmentTimelineEvent[]=[];
  let unreturnedGoalEvents=0;
  for(const assessment of assessments) {
    const date=epoch(assessment.created_at);
    if(date===null)continue;
    rows.push({
      key:'assessment:'+assessment.id,at:assessment.created_at,epoch:date,
      kind:'assessment',title:assessment.summary?.trim()||'Αξιολόγηση',
      description:'Καταχώριση αξιολόγησης',
      targetId:'academy-evaluation-'+assessment.id
    });
  }
  for(const goal of goals) {
    unreturnedGoalEvents+=Math.max(0,goal.history_count-goal.history.length);
    for(const event of goal.history) {
      const date=epoch(event.recorded_at);
      if(date===null)continue;
      rows.push({
        key:'goal:'+goal.id+':'+event.version,at:event.recorded_at,epoch:date,
        kind:'goal',title:goal.title,
        description:(eventLabels[event.event]||'Ενημέρωση στόχου')+
          ' · '+event.progress+'%'+(event.note?' · '+event.note:''),
        targetId:'development-goal-'+goal.id
      });
    }
  }
  rows.sort((a,b)=>b.epoch-a.epoch||a.key.localeCompare(b.key));
  const take=Number.isSafeInteger(limit)?Math.max(0,Math.min(limit,100)):40;
  return {
    events:rows.slice(0,take),loadedCount:rows.length,
    hiddenOlderCount:Math.max(0,rows.length-take),
    unreturnedGoalEvents
  };
}
