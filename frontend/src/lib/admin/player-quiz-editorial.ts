export type QuizOrg='lefteria_fc'|'lefteria_fc_academy';
export type QuizState='review_required'|'approved'|'published'|'draft'|'withdrawn';
export const QUIZ_TOPICS=[
  'general','positioning','defending','attacking','transition',
  'passing','movement','teamwork','rules','decision_making','goalkeeping'
] as const;
export const QUIZ_POSITIONS=[
  'all','goalkeeper','defender','fullback','winger','midfielder','striker'
] as const;
export interface AdminQuizQuestionDraft {
  prompt:string;choices:[string,string,string,string];correct_index:number;explanation:string;
}
export interface AdminQuizDraft {
  week_number:number;topic:string;position:string;title:string;questions:AdminQuizQuestionDraft[];
}
export interface AdminQuizSummary {
  id:string;title:string;week_number:number;topic:string;position:string;
  status:QuizState;question_count:number;
}
export interface AdminQuizList {
  organization_id:QuizOrg;items:AdminQuizSummary[];
}
export interface AdminQuizDetail extends AdminQuizSummary {
  organization_id:QuizOrg;
  questions:Array<AdminQuizQuestionDraft&{id:string;sequence:number}>;
}
export interface AdminQuizTransition {
  id:string;organization_id:QuizOrg;status:QuizState;
}
const uuid=(v:unknown):v is string=>typeof v==='string'&&
  /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/i.test(v);
const object=(v:unknown):v is Record<string,unknown>=>
  !!v&&typeof v==='object'&&!Array.isArray(v);
const text=(v:unknown,min:number,max:number):v is string=>
  typeof v==='string'&&v.length>=min&&v.length<=max&&
  v.trim()===v&&!/[\x00-\x1f\x7f]/.test(v);
const validTopic=(v:unknown)=>typeof v==='string'&&
  QUIZ_TOPICS.includes(v as typeof QUIZ_TOPICS[number]);
const validPosition=(v:unknown)=>typeof v==='string'&&
  QUIZ_POSITIONS.includes(v as typeof QUIZ_POSITIONS[number]);
const validState=(v:unknown)=>v==='draft'||v==='review_required'||
  v==='approved'||v==='published'||v==='withdrawn';
const validSummary=(v:unknown):v is AdminQuizSummary=>object(v)&&
  uuid(v.id)&&text(v.title,5,180)&&
  Number.isInteger(v.week_number)&&Number(v.week_number)>=1&&Number(v.week_number)<=520&&
  validTopic(v.topic)&&validPosition(v.position)&&validState(v.status)&&
  Number.isInteger(v.question_count)&&Number(v.question_count)>=0&&Number(v.question_count)<=20;
export function validAdminQuizList(v:unknown,org:QuizOrg):v is AdminQuizList {
  return object(v)&&v.organization_id===org&&Array.isArray(v.items)&&
    v.items.length<=100&&v.items.every(validSummary);
}
export function validAdminQuizQuestion(v:unknown):v is AdminQuizQuestionDraft {
  if(!object(v)||!text(v.prompt,10,500)||!Array.isArray(v.choices)||
    v.choices.length!==4||!v.choices.every(x=>text(x,1,500))||
    new Set(v.choices).size!==4||!Number.isInteger(v.correct_index)||
    Number(v.correct_index)<0||Number(v.correct_index)>3||
    typeof v.explanation!=='string'||v.explanation.length>2000||
    /[\x00-\x1f\x7f]/.test(v.explanation))return false;
  return true;
}
export function validAdminQuizDraft(v:unknown):v is AdminQuizDraft {
  if(!object(v)||!Number.isInteger(v.week_number)||
    Number(v.week_number)<1||Number(v.week_number)>520||
    !validTopic(v.topic)||!validPosition(v.position)||
    !text(v.title,5,180)||!Array.isArray(v.questions)||
    v.questions.length<3||v.questions.length>20)return false;
  return v.questions.every(validAdminQuizQuestion);
}
export function validAdminQuizDetail(v:unknown,org:QuizOrg,id:string):v is AdminQuizDetail {
  if(!validSummary(v)||!object(v)||v.id!==id||
    v.organization_id!==org||!Array.isArray(v.questions)||
    v.questions.length<3||v.questions.length>20)return false;
  const ids=new Set<string>();
  return v.questions.every((q:unknown,i:number)=>{
    if(!object(q)||!uuid(q.id)||ids.has(q.id)||q.sequence!==i+1||
       !validAdminQuizQuestion(q))return false;
    ids.add(q.id);return true;
  });
}
export function validAdminQuizTransition(v:unknown,org:QuizOrg,id:string,
  state:QuizState):v is AdminQuizTransition {
  return object(v)&&v.id===id&&v.organization_id===org&&v.status===state;
}
export function newQuizQuestion():AdminQuizQuestionDraft {
  return {prompt:'',choices:['','','',''],correct_index:0,explanation:''};
}
