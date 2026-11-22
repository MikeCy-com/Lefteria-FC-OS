<script lang="ts">
  import {parseNavigationDesign,NAVIGATION_PARENTS,type NavigationEntry,type NavigationParent}
    from '../public/navigation-design.ts';
  import type {PublicOrganization} from '../public/types.ts';
  let {value,organization,onChange,disabled=false}:{
    value:string;organization:PublicOrganization;onChange:(text:string)=>void;disabled?:boolean
  }=$props();
  const config=$derived(parseNavigationDesign(value,organization)??{links:[]});
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
    onChange(JSON.stringify({links:config.links.filter((_,i)=>i!==index)}));
  }
  function add(){
    if(disabled||config.links.length>=24)return;
    const used=new Set(config.links.map(x=>x.id));
    let n=1;while(used.has('custom-link-'+n))n++;
    const link:NavigationEntry={id:'custom-link-'+n,label:'Νέος σύνδεσμος',
      path:organization==='lefteria_fc_academy'?'/academy':'/team',
      parent:'root',order:config.links.length};
    onChange(JSON.stringify({links:[...config.links,link]}));
  }
</script>
<section class="menu-editor" data-testid="cms-menu-editor" aria-label="Διαχείριση μενού και υπομενού">
  <h3>Μενού και υπομενού οργανισμού</h3>
  <p>Προσθέστε συνδέσμους στις υπάρχουσες κατηγορίες ή στο κύριο μενού. Η δημοσίευση είναι ξεχωριστή ενέργεια. Οι λειτουργικές διαδρομές δεν αντικαθίστανται.</p>
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
.menu-editor fieldset{border:1px solid #525252;border-radius:7px;padding:12px;min-width:0}
.menu-editor legend{color:#f6bb38;font-size:13px;padding-inline:6px}
.menu-editor .fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px}
.menu-editor label{display:grid;gap:5px;font-size:12px}
.menu-editor input,.menu-editor select{width:100%;min-width:0;min-height:38px;padding:6px;border:1px solid #777;background:#171717;color:#fff;border-radius:5px}
.menu-editor button{min-height:38px;border:1px solid #f6bb38;color:#f6bb38;background:#161616;border-radius:6px;padding:7px 10px;justify-self:start}
</style>
