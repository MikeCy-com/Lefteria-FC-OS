<script lang="ts">
  import {browser} from '$app/env';
  type Owner='lefteria_fc'|'lefteria_fc_academy';
  type Preferences={
    organization_id:Owner;player_id:string;version:number;disabled:boolean;
    wish_text:string;background_color:string;emoji:string;
    public_enabled:false;auto_post_facebook:false;auto_post_instagram:false;
  };
  let {owner,playerId,playerName,actorId,onExpired,onRevoked}:{
    owner:Owner;playerId:string;playerName:string;actorId:string;
    onExpired:()=>void;onRevoked:()=>void;
  }=$props();
  const colors=['#F5A623','#EF4444','#10B981','#3B82F6',
    '#8B5CF6','#EC4899','#F97316','#0EA5E9'];
  const emojis=['🎂','🎉','🎈','🎁','🌟','⚽','🏆','❤️'];
  let value=$state<Preferences|null>(null),loading=$state(false),
    saving=$state(false),error=$state(''),notice=$state(''),revision=$state(0);
  let disabled=$state(true),wish=$state(''),color=$state('#F5A623'),emoji=$state('🎂');
  let previewOpen=$state(false);

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
    previewOpen=false;
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
    <button type="button" class="preview-toggle" disabled={saving}
      aria-expanded={previewOpen} aria-controls="player-birthday-private-preview"
      onclick={()=>previewOpen=!previewOpen}>
      {previewOpen?'Απόκρυψη ιδιωτικής προεπισκόπησης':'Προεπισκόπηση κάρτας · μόνο διαχείριση'}
    </button>
    {#if previewOpen}
      <div id="player-birthday-private-preview" class="birthday-card-wrap"
        data-testid="birthday-private-branded-preview">
        <div class="birthday-card" style={'--birthday-accent:'+color}>
          <div class="card-glow"></div>
          <div class="brand-mark">
            <span class="brand-ball" aria-hidden="true">⚽</span>
            <span>LEFTERIA <b>FC</b><small>ACADEMY · FOOTBALL FAMILY</small></span>
          </div>
          <div class="card-center">
            <div class="card-emoji" aria-hidden="true">{emoji}</div>
            <p class="card-overline">LEFTERIA FC WISHES YOU</p>
            <h5>ΧΡΟΝΙΑ ΠΟΛΛΑ!</h5>
            <strong>{playerName}</strong>
            <p class="card-wish">{wish.trim()||'Χρόνια πολλά! Υγεία, χαρά και πολλά όμορφα ποδοσφαιρικά όνειρα!'}</p>
          </div>
          <div class="card-footer">
            <span>ONE TEAM · ONE FAMILY</span><span>LEFTERIA FC</span>
          </div>
          <div class="preview-watermark">
            ΙΔΙΩΤΙΚΗ ΠΡΟΕΠΙΣΚΟΠΗΣΗ · ΟΧΙ ΓΙΑ ΔΗΜΟΣΙΕΥΣΗ
          </div>
          {#if disabled}
            <div class="disabled-shield" role="status">
              Η κάρτα είναι απενεργοποιημένη
            </div>
          {/if}
        </div>
        <p class="preview-warning">Η προεπισκόπηση υπάρχει μόνο σε αυτή την εξουσιοδοτημένη οθόνη διαχείρισης.
          Δεν δημιουργεί εικόνα προς λήψη, δεν δημοσιεύεται και δεν αποστέλλεται σε καμία πλατφόρμα.
          Οι αλλαγές που δεν έχουν αποθηκευτεί εμφανίζονται μόνο προσωρινά.</p>
      </div>
    {/if}
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
  .preview-toggle{background:transparent;color:#f5c979}
  .birthday-card-wrap{display:grid;gap:10px;max-width:900px}
  .birthday-card{position:relative;isolation:isolate;aspect-ratio:16/9;overflow:hidden;
    border:1px solid color-mix(in srgb,var(--birthday-accent) 65%,#fff);
    border-radius:14px;color:white;background:radial-gradient(ellipse at 80% 12%,
    color-mix(in srgb,var(--birthday-accent) 28%,transparent),transparent 44%),
    linear-gradient(130deg,#090b0c 8%,#19201e 51%,#060708 100%);
    box-shadow:0 18px 40px #0008;display:flex;flex-direction:column;
    padding:clamp(12px,3.5%,38px)}
  .card-glow{position:absolute;z-index:-1;inset:-40%;pointer-events:none;
    background:conic-gradient(from 40deg at 74% 65%,transparent 30%,
    color-mix(in srgb,var(--birthday-accent) 35%,transparent),transparent 58%);opacity:.5}
  .brand-mark{display:flex;align-items:center;gap:12px;font-weight:900;
    line-height:1.05;letter-spacing:.11em;font-size:clamp(10px,2.4vw,23px)}
  .brand-mark b{color:var(--birthday-accent)}
  .brand-mark small{display:block;font-size:clamp(5px,1vw,10px);
    letter-spacing:.16em;color:#d3d9d6;margin-top:5px}
  .brand-ball{font-size:clamp(18px,4.3vw,42px)}
  .card-center{display:flex;flex:1;min-height:0;align-items:center;
    justify-content:center;flex-direction:column;text-align:center;gap:clamp(3px,.9vw,11px)}
  .card-center .card-emoji{font-size:clamp(21px,5.5vw,56px);line-height:1}
  .card-overline{color:var(--birthday-accent);font-size:clamp(7px,1.3vw,14px);
    font-weight:900;letter-spacing:.23em}
  .card-center h5{font-size:clamp(19px,5vw,55px);
    letter-spacing:.035em;line-height:1;margin:0;font-weight:900;color:#fff;
    text-shadow:0 4px 18px #000a}
  .card-center strong{font-size:clamp(12px,3vw,30px);font-weight:900;
    color:var(--birthday-accent);max-width:90%;overflow-wrap:anywhere}
  .card-wish{max-width:80%;font-size:clamp(8px,1.4vw,16px);
    line-height:1.4;color:#eef2ed;overflow-wrap:anywhere;
    display:-webkit-box;-webkit-box-orient:vertical;-webkit-line-clamp:3;overflow:hidden}
  .card-footer{display:flex;justify-content:space-between;gap:8px;
    color:#bfc7c4;font-size:clamp(6px,1.2vw,11px);letter-spacing:.12em;font-weight:700}
  .preview-watermark{position:absolute;left:0;right:0;top:51%;
    text-align:center;transform:rotate(-17deg);pointer-events:none;
    font-size:clamp(10px,2.2vw,24px);font-weight:900;letter-spacing:.16em;
    color:#ffffff2a;text-shadow:0 1px 1px #0006}
  .disabled-shield{position:absolute;inset:0;background:#000a;display:grid;
    place-items:center;text-align:center;padding:15px;font-size:clamp(11px,2.2vw,23px);
    font-weight:900;color:#fff}
  .preview-warning{font-size:11px;color:#d5b879}
  @media(max-width:520px){.brand-mark{gap:5px}.birthday-card{border-radius:9px}
    .card-wish{max-width:92%}}

</style>
