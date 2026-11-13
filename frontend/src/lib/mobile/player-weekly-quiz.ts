export type PlayerQuizOrganization='lefteria_fc'|'lefteria_fc_academy';
export type PlayerQuizTopic='positioning'|'defending'|'attacking'|'transition'|
  'passing'|'movement'|'teamwork'|'rules'|'decision_making'|'goalkeeping'|'general';
export type PlayerQuizPosition='all'|'goalkeeper'|'defender'|'fullback'|
  'winger'|'midfielder'|'striker';
export interface PlayerQuizQuestion {
  id:string; sequence:number; prompt:string; choices:string[];
}
export interface PlayerQuiz {
  id:string; title:string; topic:PlayerQuizTopic; position:PlayerQuizPosition;
  week_number:number; questions:PlayerQuizQuestion[];
}
export interface PlayerQuizPage {
  organization_id:PlayerQuizOrganization;
  started:boolean;
  quiz:PlayerQuiz|null;
}
export interface PlayerQuizStart {
  organization_id:PlayerQuizOrganization;
  started_at:string;
}
export interface PlayerQuizAnswer {
  question_id:string; selected_index:number;
}
export interface PlayerQuizScore {
  organization_id:PlayerQuizOrganization; quiz_id:string;
  score:number; total:number; submitted:boolean;
}
const uuid=(v:unknown):v is string=>typeof v==='string'&&
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v);
const topics=new Set<PlayerQuizTopic>([
  'positioning','defending','attacking','transition','passing',
  'movement','teamwork','rules','decision_making','goalkeeping','general']);
const positions=new Set<PlayerQuizPosition>([
  'all','goalkeeper','defender','fullback','winger','midfielder','striker']);
const plain=(v:unknown,max:number,min=1):v is string=>
  typeof v==='string'&&v.trim().length>=min&&v.length<=max;
const object=(v:unknown):v is Record<string,unknown>=>
  !!v&&typeof v==='object'&&!Array.isArray(v);
export function validPlayerQuizPage(v:unknown,org:PlayerQuizOrganization):v is PlayerQuizPage {
  if(!object(v)||v.organization_id!==org||typeof v.started!=='boolean')return false;
  if(v.quiz===null)return true;
  if(!v.started||!object(v.quiz))return false;
  const quiz=v.quiz;
  if(!uuid(quiz.id)||!plain(quiz.title,180,5)||
    !topics.has(quiz.topic as PlayerQuizTopic)||
    !positions.has(quiz.position as PlayerQuizPosition)||
    // Only positions the server can establish from the canonical roster
    // are accepted; fine-grained winger/striker roles remain unavailable.
    !['all','defender','midfielder','goalkeeper'].includes(
      quiz.position as string)||
    !Number.isSafeInteger(quiz.week_number)||Number(quiz.week_number)<1||
    Number(quiz.week_number)>520||
    !Array.isArray(quiz.questions)||quiz.questions.length<3||
    quiz.questions.length>20)return false;
  const seen=new Set<string>();
  return quiz.questions.every((q:unknown,index:number)=>{
    if(!object(q)||!uuid(q.id)||seen.has(q.id)||
      q.sequence!==index+1||!plain(q.prompt,500,10)||
      !Array.isArray(q.choices)||q.choices.length!==4||
      !q.choices.every((x:unknown)=>plain(x,500)))return false;
    // Even a buggy API must never disclose grading material on GET.
    if('correct_index' in q||'explanation' in q||'answer' in q)return false;
    seen.add(q.id);return true;
  });
}
export function validPlayerQuizStart(v:unknown,org:PlayerQuizOrganization):v is PlayerQuizStart {
  if(!object(v)||v.organization_id!==org||
    typeof v.started_at!=='string')return false;
  const stamp=Date.parse(v.started_at);
  return Number.isFinite(stamp)&&stamp<=Date.now();
}
export function validPlayerQuizAnswers(quiz:PlayerQuiz,answers:readonly PlayerQuizAnswer[]):boolean {
  if(answers.length!==quiz.questions.length)return false;
  const chosen=new Map<string,number>();
  for(const answer of answers){
    if(!uuid(answer.question_id)||chosen.has(answer.question_id)||
      !Number.isInteger(answer.selected_index)||
      answer.selected_index<0||answer.selected_index>3)return false;
    chosen.set(answer.question_id,answer.selected_index);
  }
  return quiz.questions.every(q=>chosen.has(q.id));
}
export function validPlayerQuizScore(v:unknown,org:PlayerQuizOrganization,quiz:PlayerQuiz):v is PlayerQuizScore {
  return object(v)&&v.organization_id===org&&v.quiz_id===quiz.id&&
    v.submitted===true&&v.total===quiz.questions.length&&
    Number.isInteger(v.score)&&Number(v.score)>=0&&Number(v.score)<=Number(v.total);
}
