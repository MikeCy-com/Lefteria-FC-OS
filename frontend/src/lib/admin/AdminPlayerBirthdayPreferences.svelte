<script lang="ts">
  import {browser} from '$app/env';
  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Preferences={
    organization_id:Owner;player_id:string;version:number;disabled:boolean;
    wish_text:string;background_color:string;emoji:string;
    public_enabled:false;auto_post_facebook:false;auto_post_instagram:false;
  };
  let {owner,playerId,actorId,onExpired,onRevoked}:{
    owner:Owner;playerId:string;actorId:string;
    onExpired:()=>void;onRevoked:()=>void;
  }=$props();
  const colors=['#F5A623','#EF4444','#10B981','#3B82F6',
    '#8B5CF6','#EC4899','#F97316','#0EA5E9'];
  const emojis=['🎂','🎉','🎈','🎁','🌟','⚽','🏆','❤️'];
  let value=$state<Preferences|null>(null),loading=$state(false),
    saving=$state(false),error=$state(''),notice=$state(''),revision=$state(0);
  let disabled=$state(true),wish=$state(''),color=$state('#F5A623'),emoji=$state('🎂');

  function valid(raw:unknown,expectedOwner:Owner,expectedPlayer:string):raw is Preferences {
    if(!raw||typeof raw!=='object'||Array.isArray(raw))return false;
    const p=raw as Record<string,unknown>;
    return p.organization_id===expectedOwner&&p.player_id===expectedPlayer&&
      Number.isSafeInteger(p.version)&&(p.version as number)>=0&&
      (p.version as number)<=10000&&typeof p.disabled==='boolean'&&
      typeof p.wish_text==='string'&&p.wish_text.length<=500&&
      typeof p.background_color==='string'&&/^#[0-9A-Fa-f]{6}$/.test(p.background_color)&&
      emojis.includes(p.emoji as string)&&p.public_enabled===false&&
      p.auto_post_facebook===false&&p.auto_post_instagram===false;
  }
  $effect(()=>{
    const currentOwner=owner,currentPlayer=playerId,currentActor=actorId;
    void revision;void currentActor;
    value=null;disabled=true;wish='';color='#F5A623';emoji='🎂';
    error='';notice='';
    if(!browser||!currentPlayer)return;
    const controller=new AbortController();
    loading=true;
    void (async()=>{
      try{
        const response=await fetch('/api/admin/football/players/'+
          encodeURIComponent(currentPlayer)+'/birthday-preferences?'+
          new URLSearchParams({organization_id:currentOwner}),{
            credentials:'same-origin',cache:'no-store',signal:controller.signal
          });
        if(controller.signal.aborted)return;
        if(response.status===401){onExpired();return;}
        if(response.status===403){onRevoked();return;}
        if(!response.ok)throw Error('Δεν φορτώθηκαν οι ιδιωτικές ρυθμίσεις γενεθλίων.');
        const raw:unknown=await response.json();
        if(!valid(raw,currentOwner,currentPlayer))
          throw Error('Μη επαληθευμένες ρυθμίσεις γενεθλίων.');
        if(controller.signal.aborted)return;
        value=raw;disabled=raw.disabled;wish=raw.wish_text;
        color=raw.background_color;emoji=raw.emoji;
      }catch(e){
        if(!controller.signal.aborted)
          error=e instanceof Error?e.message:'Σφάλμα ρυθμίσεων γενεθλίων.';
      }finally{if(!controller.signal.aborted)loading=false;}
    })();
    return()=>controller.abort();
  });

  async function save(){
    if(!browser||!value||loading||saving||wish.length>500||
      /[\x00-\x1f\x7f]/.test(wish)||!/^#[0-9A-Fa-f]{6}$/.test(color)||
      !emojis.includes(emoji))return;
    const selected=value,expectedOwner=owner,expectedPlayer=playerId,expectedActor=actorId;
    saving=true;error='';notice='';
    try{
      const response=await fetch('/api/admin/football/players/'+
        encodeURIComponent(expectedPlayer)+'/birthday-preferences?'+
        new URLSearchParams({organization_id:expectedOwner}),{
          method:'PUT',credentials:'same-origin',cache:'no-store',
          headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
          body:JSON.stringify({expected_version:selected.version,disabled,
            wish_text:wish,background_color:color,emoji})
        });
      if(expectedOwner!==owner||expectedPlayer!==playerId||expectedActor!==actorId)return;
      if(response.status===401){onExpired();return;}
      if(response.status===403){onRevoked();return;}
      if(response.status===409){revision++;throw Error('Οι ρυθμίσεις άλλαξαν. Ελέγξτε τη νέα έκδοση.');}
      if(!response.ok)throw Error('Δεν αποθηκεύτηκαν οι ρυθμίσεις γενεθλίων.');
      const raw:unknown=await response.json();
      if(!valid(raw,expectedOwner,expectedPlayer)||raw.version!==selected.version+1||
        raw.disabled!==disabled||raw.wish_text!==wish||
        raw.background_color!==color||raw.emoji!==emoji)
        throw Error('Η απόκριση αποθήκευσης δεν επαληθεύτηκε.');
      value=raw;notice='Οι ιδιωτικές ρυθμίσεις αποθηκεύτηκαν. Καμία κάρτα δεν δημοσιεύτηκε.';
    }catch(e){error=e instanceof Error?e.message:'Σφάλμα αποθήκευσης.';}
    finally{saving=false;}
  }
</script>

<section class="birthday-private" data-testid="birthday-private-preferences">
  <h4>🎂 Κάρτα γενεθλίων · Ιδιωτικές ρυθμίσεις</h4>
  <p>Αρχικά απενεργοποιημένη. Η αποθήκευση ΔΕΝ ενεργοποιεί δημόσια προβολή ή αυτόματη ανάρτηση στα κοινωνικά δίκτυα.</p>
  {#if loading}<p role="status">Φόρτωση…</p>{/if}
  {#if error}<p role="alert">{error}</p>{/if}
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if value}
    <label><input type="checkbox" bind:checked={disabled} disabled={saving}/>
      Απενεργοποίηση κάρτας γενεθλίων (προστασία ιδιωτικότητας)</label>
    <label>Ευχή στα ελληνικά
      <textarea rows="2" maxlength="500" bind:value={wish} disabled={saving}/>
    </label>
    <label>Χρώμα φόντου
      <select bind:value={color} disabled={saving}>
        {#each colors as c}<option value={c}>{c}</option>{/each}
      </select>
    </label>
    <label>Σύμβολο
      <select bind:value={emoji} disabled={saving}>
        {#each emojis as e}<option value={e}>{e}</option>{/each}
      </select>
    </label>
    <p>Δημόσια προβολή: <strong>ανενεργή</strong> · Facebook / Instagram: <strong>ανενεργά</strong></p>
    <button type="button" disabled={saving} onclick={()=>void save()}>
      {saving?'Αποθήκευση…':'Αποθήκευση ιδιωτικών ρυθμίσεων'}
    </button>
  {/if}
</section>
<style>
  .birthday-private{border:1px solid #574529;border-radius:10px;padding:14px;display:grid;gap:10px;background:#17191a}
  h4{margin:0;color:#ffca46}
  p{margin:0;font-size:12px;line-height:1.6;color:#c3c5c5}
  label{display:grid;gap:6px;font-size:12px;color:#e3c88c}
  label:has(input){display:flex;align-items:center}
  textarea,select{background:#111;color:#fff;border:1px solid #555;border-radius:6px;padding:9px;min-height:42px;max-width:100%}
  button{min-height:44px;border:1px solid #d5a448;border-radius:7px;background:#3b2c17;color:#fff;cursor:pointer}
  button:disabled{opacity:.5}
</style>
