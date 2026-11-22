<script lang="ts">
  import {NAVIGATION_PARENTS,BUILTIN_CATEGORY_IDS,validBuiltinCategoryOrder,type BuiltinCategoryId,type NavigationDesign,type NavigationEntry,type NavigationParent}
    from '../public/navigation-design.ts';
  import type {PublicOrganization} from '../public/types.ts';
  let {value,organization,onChange,disabled=false}:{
    value:string;organization:PublicOrganization;onChange:(text:string)=>void;disabled?:boolean
  }=$props();
  // Keep in-progress text edits visible even when the unsaved draft is invalid.
  // Save/publish still uses the strict owner-aware parser and backend policy.
  const config=$derived((()=>{
    try {
      const candidate=JSON.parse(value) as NavigationDesign;
      return Array.isArray(candidate?.links)?{links:candidate.links,
        category_order:validBuiltinCategoryOrder(candidate.category_order)?candidate.category_order:[...BUILTIN_CATEGORY_IDS],
        category_labels:candidate.category_labels??{}}
        :{links:[] as NavigationEntry[],category_order:[...BUILTIN_CATEGORY_IDS],category_labels:{} as Record<string,string>};
    } catch {return {links:[] as NavigationEntry[],category_order:[...BUILTIN_CATEGORY_IDS],category_labels:{} as Record<string,string>};}
  })());
  const parentNames:Record<NavigationParent,string>={
    root:'Κύριο μενού',club:'Σύλλογος',team:'Πρώτη ομάδα',academy:'Ακαδημία',
    matches:'Αγώνες',news:'Νέα',events:'Εκδηλώσεις',shop:'Κατάστημα',contact:'Επικοινωνία'
  };
  const allowed=$derived(NAVIGATION_PARENTS.filter(parent=>organization==='lefteria_fc_academy'?
    !['team','matches','shop'].includes(parent):parent!=='academy'));
  function update(index:number,patch:Partial<NavigationEntry>){
    const next=structuredClone(config);
    next.links[index]={...next.links[index],...patch};
    onChange(JSON.stringify(next));
  }
  function remove(index:number){
    onChange(JSON.stringify({...config,links:config.links.filter((_,i)=>i!==index)}));
  }
  function renameCategory(id:BuiltinCategoryId,label:string){
    if(disabled)return;
    const next=structuredClone(config);
    next.category_labels={...next.category_labels,[id]:label};
    onChange(JSON.stringify(next));
  }
  function moveCategory(index:number,direction:-1|1){
    if(disabled)return;
    const other=index+direction;
    if(other<0||other>=config.category_order.length)return;
    const next=structuredClone(config);
    [next.category_order[index],next.category_order[other]]=
      [next.category_order[other],next.category_order[index]];
    onChange(JSON.stringify(next));
  }
  function add(){
    if(disabled||config.links.length>=24)return;
    const used=new Set(config.links.map(x=>x.id));
    let n=1;while(used.has('custom-link-'+n))n++;
    const link:NavigationEntry={id:'custom-link-'+n,label:'Νέος σύνδεσμος',
      path:organization==='lefteria_fc_academy'?'/academy':'/team',
      parent:'root',order:config.links.length};
    onChange(JSON.stringify({...config,links:[...config.links,link]}));
  }
</script>
<section class="menu-editor" data-testid="cms-menu-editor" aria-label="Διαχείριση μενού και υπομενού">
  <h3>Μενού και υπομενού οργανισμού</h3>
  <p>Προσθέστε συνδέσμους στις υπάρχουσες κατηγορίες ή στο κύριο μενού. Η δημοσίευση είναι ξεχωριστή ενέργεια. Οι λειτουργικές διαδρομές δεν αντικαθίστανται.</p>
  <fieldset data-testid="cms-menu-category-order">
    <legend>Σειρά βασικών κατηγοριών</legend>
    <p>Οι εννέα βασικές κατηγορίες διατηρούνται πάντα. Μπορείτε να αλλάξετε μόνο τη σειρά τους.</p>
    <ol class="category-order">
      {#each config.category_order as id,index (id)}
        <li>
          <label class="category-label">
            <span>{id}</span>
            <input aria-label={'Ετικέτα κατηγορίας '+id} maxlength="40" disabled={disabled}
              value={config.category_labels[id]??(parentNames[id as NavigationParent]??'Αρχική')}
              oninput={e=>renameCategory(id,e.currentTarget.value)}/>
          </label>
          <button type="button" disabled={disabled||index===0}
            aria-label={'Μετακίνηση '+id+' πάνω'} onclick={()=>moveCategory(index,-1)}>↑</button>
          <button type="button" disabled={disabled||index===config.category_order.length-1}
            aria-label={'Μετακίνηση '+id+' κάτω'} onclick={()=>moveCategory(index,1)}>↓</button>
        </li>
      {/each}
    </ol>
  </fieldset>
  {#each config.links as link,index (link.id)}
    <fieldset>
      <legend>Σύνδεσμος {index+1}</legend>
      <div class="fields">
        <label>Ετικέτα
          <input maxlength="80" disabled={disabled} value={link.label}
            oninput={e=>update(index,{label:e.currentTarget.value})}/>
        </label>
        <label>Εσωτερική διαδρομή
          <input maxlength="250" disabled={disabled} value={link.path} placeholder="/about"
            oninput={e=>update(index,{path:e.currentTarget.value})}/>
        </label>
        <label>Θέση
          <select disabled={disabled} value={link.parent}
            onchange={e=>update(index,{parent:e.currentTarget.value as NavigationParent})}>
            {#each allowed as parent (parent)}<option value={parent}>{parentNames[parent]}</option>{/each}
          </select>
        </label>
        <label>Σειρά
          <input type="number" min="0" max="100" step="1" disabled={disabled} value={link.order}
            onchange={e=>update(index,{order:Number(e.currentTarget.value)})}/>
        </label>
      </div>
      <button type="button" disabled={disabled} onclick={()=>remove(index)}>Αφαίρεση συνδέσμου</button>
    </fieldset>
  {/each}
  <button type="button" disabled={disabled||config.links.length>=24} onclick={add}>+ Προσθήκη συνδέσμου</button>
  <p>Μέγιστο 24 επιπλέον σύνδεσμοι. Υποστηρίζεται ένα επίπεδο υπομενού. Οι προστατευμένες διαδρομές και σύνδεσμοι άλλου οργανισμού απορρίπτονται.</p>
</section>
<style>
.menu-editor{display:grid;gap:12px;border:1px solid #594a2d;border-radius:10px;padding:14px}
.menu-editor .category-order{display:grid;gap:6px;list-style:none;padding:0;margin:8px 0}
.menu-editor .category-order li{display:flex;align-items:center;gap:8px}
.menu-editor .category-order .category-label{flex:1;display:flex;align-items:center;gap:8px;min-width:0}
.menu-editor .category-order .category-label span{font-size:10px;min-width:55px}
.menu-editor .category-order li span{flex:1}
.menu-editor .category-order li button{min-width:38px}
.menu-editor fieldset{border:1px solid #525252;border-radius:7px;padding:12px;min-width:0}
.menu-editor legend{color:#f6bb38;font-size:13px;padding-inline:6px}
.menu-editor .fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px}
.menu-editor label{display:grid;gap:5px;font-size:12px}
.menu-editor input,.menu-editor select{width:100%;min-width:0;min-height:38px;padding:6px;border:1px solid #777;background:#171717;color:#fff;border-radius:5px}
.menu-editor button{min-height:38px;border:1px solid #f6bb38;color:#f6bb38;background:#161616;border-radius:6px;padding:7px 10px;justify-self:start}
</style>
