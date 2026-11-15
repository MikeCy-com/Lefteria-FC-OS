/** Read-only, server-verified football classroom. Never use this DTO as authority. */
export type PlayerClassroomOrganization='lefteria_fc'|'lefteria_fc_academy';
export type PlayerClassroomTopic=
  'positioning'|'defending'|'attacking'|'transition'|'passing'|'movement'|
  'teamwork'|'rules'|'decision_making'|'goalkeeping'|'general';
export const classroomTopics:readonly PlayerClassroomTopic[]=[
  'positioning','defending','attacking','transition','passing','movement',
  'teamwork','rules','decision_making','goalkeeping','general'
];
export const classroomLabels:Record<PlayerClassroomTopic,{name:string,description:string}>={
  positioning:{name:'Τοποθέτηση',description:'Γραμμές, χώροι και επιλογή θέσης στο γήπεδο.'},
  defending:{name:'Άμυνα',description:'Πίεση, κάλυψη και σωστή απόσταση από τον αντίπαλο.'},
  attacking:{name:'Επίθεση',description:'Δημιουργία ευκαιριών και αξιοποίηση χώρων.'},
  transition:{name:'Μεταβάσεις',description:'Αλλαγή από άμυνα σε επίθεση και αντίστροφα.'},
  passing:{name:'Πάσα',description:'Στήριξη συμπαίκτη, πρώτη επαφή και συνεργασία.'},
  movement:{name:'Κίνηση',description:'Κίνηση χωρίς την μπάλα και επιλογές υποστήριξης.'},
  teamwork:{name:'Ομαδικότητα',description:'Επικοινωνία, ρόλοι και κοινή ευθύνη.'},
  rules:{name:'Κανόνες παιχνιδιού',description:'Σεβασμός, κανονισμοί και δίκαιο παιχνίδι.'},
  decision_making:{name:'Αποφάσεις',description:'Ανάγνωση του παιχνιδιού και επιλογή δράσης.'},
  goalkeeping:{name:'Τερματοφύλακας',description:'Θέση, αντίδραση και οργάνωση της άμυνας.'},
  general:{name:'Ποδοσφαιρικές βάσεις',description:'Κατανόηση του παιχνιδιού και βασικές αρχές.'}
};
export type PlayerClassroomPosition=
  'all'|'goalkeeper'|'defender'|'fullback'|'winger'|'midfielder'|'striker';
export const playerClassroomPositionLabels:Record<PlayerClassroomPosition,string>={
  all:'Γενικό πρόγραμμα',goalkeeper:'Τερματοφύλακας',defender:'Αμυντικός',
  fullback:'Πλάγιος αμυντικός',winger:'Εξτρέμ',midfielder:'Μέσος',striker:'Επιθετικός'
};
export interface PlayerClassroomSubject {
  topic:PlayerClassroomTopic;
  published_quizzes:number;
  completed_quizzes:number;
  correct_answers:number;
  total_answers:number;
}
export interface PlayerClassroomPage {
  organization_id:PlayerClassroomOrganization;
  audience:'player';
  verified_position:PlayerClassroomPosition;
  lessons:{
    approved:number;
    completed:number;
    next_title:string|null;
    next_kind:'article'|'course'|null;
  };
  quizzes:{
    started:boolean;
    completed_weeks:number;
    current_week:number;
    next_quiz_title:string|null;
    next_quiz_topic:PlayerClassroomTopic|null;
  };
  subjects:PlayerClassroomSubject[];
}
const int=(v:unknown,max:number)=>typeof v==='number'&&
  Number.isSafeInteger(v)&&v>=0&&v<=max;
const str=(v:unknown,max:number)=>typeof v==='string'&&
  v.trim().length>0&&v.length<=max;
const isObject=(v:unknown):v is Record<string,unknown>=>
  !!v&&typeof v==='object'&&!Array.isArray(v);
const topics=new Set<string>(classroomTopics);
const positions=new Set<string>(Object.keys(playerClassroomPositionLabels));
export function validPlayerClassroomPage(value:unknown,expected:PlayerClassroomOrganization):
  value is PlayerClassroomPage {
  if(!isObject(value)||value.organization_id!==expected||
     value.audience!=='player'||!positions.has(String(value.verified_position))||
     !isObject(value.lessons)||!isObject(value.quizzes)||!Array.isArray(value.subjects)||
     value.subjects.length!==classroomTopics.length)return false;
  const {lessons,quizzes,subjects}=value;
  if(!int(lessons.approved,100000)||!int(lessons.completed,100000)||
     (lessons.completed as number)>(lessons.approved as number)||
     (lessons.next_title!==null&&!str(lessons.next_title,180))||
     (lessons.next_kind!==null&&lessons.next_kind!=='article'&&lessons.next_kind!=='course')||
     (lessons.next_title===null)!==(lessons.next_kind===null))return false;
  if(typeof quizzes.started!=='boolean'||
     !int(quizzes.completed_weeks,520)||!int(quizzes.current_week,521)||
     quizzes.current_week!==(quizzes.completed_weeks as number)+1||
     (quizzes.next_quiz_title!==null&&!str(quizzes.next_quiz_title,180))||
     (quizzes.next_quiz_topic!==null&&!topics.has(String(quizzes.next_quiz_topic)))||
     (quizzes.next_quiz_title===null)!==(quizzes.next_quiz_topic===null)||
     (!quizzes.started&&(quizzes.completed_weeks!==0||quizzes.next_quiz_title!==null)))
    return false;
  const seen=new Set<string>();
  for(const subject of subjects){
    if(!isObject(subject)||!topics.has(String(subject.topic))||
       seen.has(String(subject.topic))||!int(subject.published_quizzes,520)||
       !int(subject.completed_quizzes,520)||
       (subject.completed_quizzes as number)>(subject.published_quizzes as number)||
       !int(subject.correct_answers,10400)||!int(subject.total_answers,10400)||
       (subject.correct_answers as number)>(subject.total_answers as number))
      return false;
    seen.add(String(subject.topic));
  }
  return seen.size===topics.size;
}
