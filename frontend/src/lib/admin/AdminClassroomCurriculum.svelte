<script lang="ts">
  import {browser} from '$app/env';
  import {classroomTopics,classroomLabels} from '../mobile/player-classrooms.ts';
  import type {PlayerClassroomTopic} from '../mobile/player-classrooms.ts';

  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Lesson={id:string;title:string;kind:'article'|'course';revision_id:string;
    topic:PlayerClassroomTopic|null;version:number;needs_reverification:boolean};
  type Page={organization_id:Owner;offset:number;limit:number;total:number;items:Lesson[]};
  const topics=new Set<string>(classroomTopics);
  const uuid=(v:unknown)=>typeof v==='string'&&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v);
  const obj=(v:unknown):v is Record<string,unknown>=>!!v&&typeof v==='object'&&!Array.isArray(v);
  function validPage(v:unknown,owner:Owner,at:number):v is Page {
    if(!obj(v)||v.organization_id!==owner||v.offset!==at||v.limit!==20||
      !Number.isSafeInteger(v.total)||(v.total as number)<0||
      (v.total as number)>250000||!Array.isArray(v.items)||v.items.length>20)return false;
    const seen=new Set<string>();
    return v.items.every((item:unknown)=>obj(item)&&uuid(item.id)&&
      !seen.has(String(item.id))&&seen.add(String(item.id))&&
      uuid(item.revision_id)&&typeof item.title==='string'&&
      item.title.length>0&&item.title.length<=180&&
      (item.kind==='course'||item.kind==='article')&&
      (item.topic===null||topics.has(String(item.topic)))&&
      Number.isSafeInteger(item.version)&&(item.version as number)>=0&&
      (item.version as number)<Number.MAX_SAFE_INTEGER&&
      typeof item.needs_reverification==='boolean'&&
      (!item.needs_reverification||item.topic===null));
  }

  let {organization,actorId,onExpired,onRevoked}:{
    organization:Owner;actorId:string;onExpired:()=>void;onRevoked:()=>void
  }=$props();
  let items=$state<Lesson[]>([]),total=$state(0),offset=$state(0),reload=$state(0);
  let selected=$state<Lesson|null>(null);
  let subject=$state<'unassigned'|PlayerClassroomTopic>('unassigned');
  let reason=$state('');
  let busy=$state(false),loading=$state(false),error=$state(''),success=$state('');

  $effect(()=>{
    const owner=organization,actor=actorId,at=offset;
    void reload;
    if(!browser||!actor||!owner)return;
    const controller=new AbortController();
    items=[];selected=null;error='';loading=true;
    void(async()=>{
      try{
        const response=await fetch('/api/admin/hub/classroom-curriculum?'+
          new URLSearchParams({organization_id:owner,offset:String(at),limit:'20'}),{
          credentials:'same-origin',cache:'no-store',signal:controller.signal});
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw new Error();
        const value:unknown=await response.json();
        if(!validPage(value,owner,at))throw new Error();
        if(!controller.signal.aborted&&owner===organization&&actor===actorId){
          items=value.items;total=value.total;
        }
      }catch{
        if(!controller.signal.aborted)error='Δεν επιβεβαιώθηκε ο κατάλογος εγκεκριμένων μαθημάτων.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });
  function choose(item:Lesson){
    if(busy||loading)return;
    selected=item;subject=item.topic??'unassigned';reason='';success='';error='';
  }
  async function save(){
    const selectedLesson=selected,owner=organization,actor=actorId;
    if(!selectedLesson||busy||loading||!items.some(x=>x.id===selectedLesson.id)||
      !(subject==='unassigned'||topics.has(subject))||
      ((subject==='unassigned'?null:subject)===selectedLesson.topic&&!selectedLesson.needs_reverification)||
      reason.trim()!==reason||reason.length<8||reason.length>250||
      /[\x00-\x1f\x7f]/.test(reason))return;
    if(!confirm('Να καταχωριστεί η ελεγμένη εκπαιδευτική θεματική; Δεν γίνεται νέα δημοσίευση.'))return;
    busy=true;error='';success='';
    try{
      const response=await fetch('/api/admin/hub/classroom-curriculum/'+
        encodeURIComponent(selectedLesson.id)+'?'+
        new URLSearchParams({organization_id:owner}),{
        method:'PUT',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({
          revision_id:selectedLesson.revision_id,
          expected_version:selectedLesson.version,topic:subject,reason
        })
      });
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(response.status===409){
        throw new Error('Το μάθημα ή η κατηγοριοποίηση άλλαξαν. Ανανέωσε πριν αποθηκεύσεις.');
      }
      if(!response.ok)throw new Error('Η κατηγοριοποίηση δεν επιβεβαιώθηκε.');
      const result:unknown=await response.json();
      if(!obj(result)||result.id!==selectedLesson.id||
        result.organization_id!==owner||result.revision_id!==selectedLesson.revision_id||
        result.topic!==(subject==='unassigned'?null:subject)||
        result.version!==selectedLesson.version+1||
        result.needs_reverification!==false)throw new Error();
      if(owner===organization&&actor===actorId){
        selected=null;reason='';
        success='Η ταξινόμηση αποθηκεύτηκε με ανθρώπινη επαλήθευση. Η σειρά μαθημάτων δεν άλλαξε.';
        reload++;
      }
    }catch(e){
      if(owner===organization&&actor===actorId)
        error=e instanceof Error&&e.message?e.message:'Η κατηγοριοποίηση δεν αποθηκεύτηκε.';
    }finally{busy=false;}
  }
</script>

<section class="curriculum" data-testid="hub-classroom-curriculum-admin" aria-labelledby="curriculum-title">
  <header>
    <p class="eyebrow">HUB · ΠΑΙΔΙΚΕΣ & ΑΓΩΝΙΣΤΙΚΕΣ ΤΑΞΕΙΣ</p>
    <h2 id="curriculum-title">Μαθήματα ανά αίθουσα</h2>
    <p>Μόνο Super Admin: αντιστοίχιση ήδη <strong>ανθρώπινα εγκεκριμένων και δημοσιευμένων</strong>
      μαθημάτων με ποδοσφαιρικές θεματικές. Δεν ξεκλειδώνει μαθήματα, δεν παρακάμπτει
      την ατομική σειρά και δεν δημοσιεύει υλικό.</p>
  </header>
  <div class="toolbar">
    <button type="button" disabled={loading||busy||offset===0}
      onclick={()=>offset=Math.max(0,offset-20)}>Προηγούμενα</button>
    <span>Μαθήματα: {total}{#if total>0} · σελίδα {Math.floor(offset/20)+1}{/if}</span>
    <button type="button" disabled={loading||busy||offset+20>=total}
      onclick={()=>offset+=20}>Επόμενα</button>
    <button type="button" disabled={loading||busy} onclick={()=>reload++}>Ανανέωση</button>
  </div>
  {#if loading}<p role="status">Φόρτωση εγκεκριμένου καταλόγου…</p>{/if}
  {#if !loading&&items.length===0}<p class="hint">Δεν υπάρχουν δημοσιευμένα μαθήματα που πληρούν τα κριτήρια για αθλητές.</p>{/if}
  <div class="lessons">
    {#each items as item (item.id)}
      <article class:selected={selected?.id===item.id}>
        <div>
          <strong>{item.title}</strong>
          <p>{item.kind==='course'?'Εκπαιδευτικό μάθημα':'Εκπαιδευτικό άρθρο'} ·
            {item.topic?classroomLabels[item.topic].name:'Χωρίς εγκεκριμένη αίθουσα'}</p>
          {#if item.needs_reverification}
            <p class="warning">Το μάθημα αναθεωρήθηκε· απαιτείται νέα αντιστοίχιση.</p>
          {/if}
        </div>
        <button type="button" disabled={busy||loading} aria-pressed={selected?.id===item.id}
          onclick={()=>choose(item)}>Επιλογή</button>
      </article>
    {/each}
  </div>
  {#if selected}
    <div class="editor">
      <h3>Ταξινόμηση: {selected.title}</h3>
      <p class="hint">Μόνο η τελευταία ελεγμένη αναθεώρηση μπορεί να ταξινομηθεί.</p>
      <label for="classroom-topic">Ποδοσφαιρική αίθουσα</label>
      <select id="classroom-topic" bind:value={subject} disabled={busy}>
        <option value="unassigned">Χωρίς αίθουσα / Αφαίρεση ταξινόμησης</option>
        {#each classroomTopics as topic (topic)}
          <option value={topic}>{classroomLabels[topic].name}</option>
        {/each}
      </select>
      <label for="classroom-topic-reason">Αιτιολογία επαλήθευσης (8–250 χαρακτήρες)</label>
      <textarea id="classroom-topic-reason" rows="3" maxlength="250" bind:value={reason}
        disabled={busy} placeholder="Επιβεβαιώθηκε η θεματική μετά τον ανθρώπινο έλεγχο περιεχομένου"></textarea>
      <button class="primary" type="button" disabled={busy||loading||
        ((subject==='unassigned'?null:subject)===selected.topic&&!selected.needs_reverification)||
        reason.length<8||reason.trim()!==reason}
        onclick={()=>void save()}>Αποθήκευση με ιστορικό</button>
    </div>
  {/if}
  {#if success}<p role="status" class="success">{success}</p>{/if}
  {#if error}<p role="alert" class="error">{error}</p>{/if}
  <p class="hint">Η αλλαγή κατηγορίας δεν αλλάζει την ήδη εγκεκριμένη σειρά μαθημάτων.
    Αποσυρμένα ή νέα μη εγκεκριμένα κείμενα δεν προσμετρώνται στις αίθουσες.</p>
</section>

<style>
  .curriculum{padding:20px;border:1px solid var(--lf-line);border-radius:14px;
    background:var(--lf-surface);color:var(--lf-text);margin:20px 0}
  h2{font:750 25px var(--lf-heading);margin:6px 0 10px}
  h3{font:700 20px var(--lf-heading)}
  .eyebrow{font-size:11px;letter-spacing:.1em;color:var(--lf-gold);font-weight:700}
  p{font-size:14px;line-height:1.6;color:var(--lf-muted)}
  .toolbar{display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin:15px 0}
  button{min-height:44px;border:1px solid var(--lf-brand-edge);border-radius:9px;
    background:var(--lf-elevated);color:var(--lf-gold-light);padding:10px 14px;cursor:pointer}
  button:disabled{opacity:.5;cursor:default}
  button:focus-visible,select:focus-visible,textarea:focus-visible{outline:2px solid var(--lf-gold);outline-offset:3px}
  .lessons{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,300px),1fr));gap:10px}
  .lessons article{border:1px solid var(--lf-line);background:var(--lf-elevated);border-radius:10px;
    padding:12px;display:flex;align-items:center;justify-content:space-between;gap:12px}
  .lessons article.selected{border-color:var(--lf-gold)}
  .lessons strong{font-size:15px;overflow-wrap:anywhere}
  .lessons p{margin:6px 0 0;font-size:12px}
  .warning,.error{color:#ffc2b1}
  .editor{border-top:1px solid var(--lf-line);padding-top:13px;margin-top:20px}
  label{font-size:14px;font-weight:700;display:block;margin:14px 0 6px}
  select,textarea{display:block;max-width:650px;width:100%;min-height:44px;
    padding:10px;border:1px solid var(--lf-line);border-radius:9px;
    background:var(--lf-elevated);color:var(--lf-text);font:inherit}
  .primary{margin-top:14px;font-weight:750;border-color:var(--lf-gold)}
  .success{color:var(--lf-gold-light)}
  .hint{font-size:12px}
</style>
