<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import Icon from '../../../lib/public/PublicIcon.svelte';
  import type { PublicPageData } from '../../../lib/public/types';

  let { data }: { data: PublicPageData & {editorialLead:string|null} } = $props();
  const steps = [
    { label: 'Παίκτης', icon: 'user' }, { label: 'Γονέας', icon: 'users' },
    { label: 'Ιατρικά', icon: 'heart' }, { label: 'Όροι', icon: 'file' },
    { label: 'Πληρωμή', icon: 'bag' }
  ] as const;
  let currentStep = $state(1);
  let pending = $state(false);
  let submitted = $state(false);
  let receipt = $state('');
  let submissionError = $state('');
  let errors = $state<Record<string, string>>({});
  type AdditionalPlayer = {
    player_first_name:string;player_last_name:string;player_dob:string;
    player_gender:string;player_address:string;player_city:string;player_postal_code:string;
    preferred_number:string;preferred_print_name:string;
    has_allergies:boolean;allergies_details:string;
    has_conditions:boolean;conditions_details:string;
    has_medication:boolean;medication_details:string;
  };
  let additionalPlayers = $state<AdditionalPlayer[]>([]);
  const numberValid = (number:string) => /^[0-9]{1,2}$/.test(number.trim());
  const printNameValid = (name:string) => name.trim().length>0 && name.trim().length<=32;
  const playerCount = $derived(1+additionalPlayers.length);
  const monthlyFee = $derived(playerCount===1?60:100+(playerCount-2)*50);
  const registrationFees = $derived(playerCount*20);
  const kitAndPrintingFees = $derived(playerCount*150);
  const initialTotal = $derived(monthlyFee+registrationFees+kitAndPrintingFees);
  function addPlayer(){
    if(pending||additionalPlayers.length>=4)return;
    additionalPlayers=[...additionalPlayers,{
      player_first_name:'',player_last_name:'',player_dob:'',player_gender:'',
      player_address:form.player_address,player_city:form.player_city,
      player_postal_code:form.player_postal_code,preferred_number:'',
      preferred_print_name:'',has_allergies:false,allergies_details:'',
      has_conditions:false,conditions_details:'',has_medication:false,medication_details:''
    }];
    errors={};
  }
  function removePlayer(index:number){
    if(pending)return;
    additionalPlayers=additionalPlayers.filter((_,i)=>i!==index);
    errors={};
  }
  let drawing = false;
  let canvas = $state<HTMLCanvasElement | undefined>();
  let form = $state({
    player_first_name:'',player_last_name:'',player_dob:'',player_gender:'',
    player_address:'',player_city:'',player_postal_code:'',
    preferred_number:'',preferred_print_name:'',
    parent_name:'',parent_relationship:'',parent_phone:'',parent_email:'',
    emergency_name:'',emergency_phone:'',emergency_relationship:'',
    has_allergies:false,allergies_details:'',has_conditions:false,conditions_details:'',
    has_medication:false,medication_details:'',
    consent_participation:false,consent_medical_auth:false,consent_gdpr:false,
    consent_media:'',consent_communications:false,comm_email:false,comm_sms:false,
    consent_liability:false,consent_financial:false,
    payment_method:'cash',signature_data:'',signature_date:''
  });
  const required = (value:string) => value.trim().length > 0;
  function validate(step:number) {
    const issues:Record<string,string> = {};
    const must = (key: keyof typeof form, valid:boolean) => { if(!valid) issues[key] = 'Υποχρεωτικό πεδίο'; };
    if(step===1) {
      for(const key of ['player_first_name','player_last_name','player_address','player_city'] as const) must(key,required(form[key]));
      must('player_dob',!!form.player_dob && /^\d{4}-\d\d-\d\d$/.test(form.player_dob) && form.player_dob < new Date().toISOString().slice(0,10));
      must('player_gender', ['male','female'].includes(form.player_gender));
      must('preferred_number',numberValid(form.preferred_number));
      must('preferred_print_name',printNameValid(form.preferred_print_name));
      for(const [i,child] of additionalPlayers.entries()){
        for(const key of ['player_first_name','player_last_name','player_address','player_city','preferred_print_name'] as const)
          if(!required(child[key]))issues['child_'+i+'_'+key]='Υποχρεωτικό πεδίο';
        if(!/^\\d{4}-\\d\\d-\\d\\d$/.test(child.player_dob) ||
          child.player_dob>=new Date().toISOString().slice(0,10))
          issues['child_'+i+'_player_dob']='Ελέγξτε την ημερομηνία γέννησης';
        if(!['male','female'].includes(child.player_gender))
          issues['child_'+i+'_player_gender']='Επιλέξτε φύλο';
        if(!numberValid(child.preferred_number))
          issues['child_'+i+'_preferred_number']='Αριθμός από 0 έως 99';
        if(!printNameValid(child.preferred_print_name))
          issues['child_'+i+'_preferred_print_name']='Συμπληρώστε όνομα εκτύπωσης (έως 32 χαρακτήρες)';
      }
    }
    if(step===2) {
      for(const key of ['parent_name','parent_relationship','emergency_name'] as const) must(key,required(form[key]));
      for(const key of ['parent_phone','emergency_phone'] as const) must(key, /^\+?[0-9 ()-]{7,32}$/.test(form[key].trim()));
      must('parent_email',/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(form.parent_email.trim()));
    }
    if(step===3) {
      if(form.has_allergies) must('allergies_details',required(form.allergies_details));
      if(form.has_conditions) must('conditions_details',required(form.conditions_details));
      if(form.has_medication) must('medication_details',required(form.medication_details));
      for(const [i,child] of additionalPlayers.entries()){
        for(const [flag,field] of [
          ['has_allergies','allergies_details'],['has_conditions','conditions_details'],['has_medication','medication_details']
        ] as const)if(child[flag]&&!required(child[field]))
          issues['child_'+i+'_'+field]='Συμπληρώστε τις ιατρικές πληροφορίες για '+(child.player_first_name||'το παιδί');
      }
    }
    if(step===4) {
      for(const key of ['consent_participation','consent_medical_auth','consent_gdpr','consent_liability','consent_financial'] as const) must(key,form[key]);
      must('consent_media',form.consent_media==='yes'||form.consent_media==='no');
      if(form.consent_communications && !form.comm_email && !form.comm_sms) issues.consent_communications='Επιλέξτε email ή SMS.';
    }
    if(step===5) {
      must('payment_method',['cash','card','transfer'].includes(form.payment_method));
      must('signature_data',form.signature_data.startsWith('data:image/png;base64,') && form.signature_data.length>150);
      must('signature_date',!!form.signature_date);
    }
    errors=issues;
    return Object.keys(issues).length===0;
  }
  function next() {
    if(validate(currentStep)) currentStep=Math.min(5,currentStep+1);
  }
  function previous(){ errors={}; currentStep=Math.max(1,currentStep-1); }

  function beginSignature(event:PointerEvent) {
    if(pending) return;
    const el=event.currentTarget as HTMLCanvasElement;
    const rect=el.getBoundingClientRect();
    if(!el.width || el.width!==Math.round(rect.width*devicePixelRatio)) {
      el.width=Math.round(rect.width*devicePixelRatio);el.height=Math.round(150*devicePixelRatio);
      form.signature_data='';
    }
    const ctx=el.getContext('2d');if(!ctx)return;
    ctx.strokeStyle='#f6bb38';ctx.lineWidth=2.5*devicePixelRatio;ctx.lineCap='round';ctx.lineJoin='round';
    ctx.beginPath();ctx.moveTo((event.clientX-rect.left)*devicePixelRatio,(event.clientY-rect.top)*devicePixelRatio);
    drawing=true;el.setPointerCapture(event.pointerId);
  }
  function drawSignature(event:PointerEvent) {
    if(!drawing)return;
    const el=event.currentTarget as HTMLCanvasElement,ctx=el.getContext('2d');
    if(!ctx)return;
    const rect=el.getBoundingClientRect();
    ctx.lineTo((event.clientX-rect.left)*devicePixelRatio,(event.clientY-rect.top)*devicePixelRatio);
    ctx.stroke();
  }
  function finishSignature(event:PointerEvent) {
    if(!drawing)return;
    drawing=false;
    const el=event.currentTarget as HTMLCanvasElement;
    form.signature_data=el.toDataURL('image/png');
    errors={...errors,signature_data:''};
  }
  function clearSignature(){canvas?.getContext('2d')?.clearRect(0,0,canvas.width,canvas.height);form.signature_data='';}
  async function submit(event:SubmitEvent) {
    event.preventDefault();
    if(pending)return;
    for(let step=1;step<=5;step++)if(!validate(step)){currentStep=step;return;}
    pending=true;submissionError='';
    try{
      const response=await fetch('/api/public/academy/registration',{
        method:'POST',headers:{'content-type':'application/json'},credentials:'omit',
        body:JSON.stringify({...form,additional_players:additionalPlayers}),cache:'no-store'
      });
      const result=await response.json().catch(()=>null);
      if(response.status===202 && typeof result?.receipt==='string' && result.receipt &&
        Array.isArray(result.receipts) && result.receipts.length===playerCount){
        receipt=result.receipt;submitted=true;
      }else if(response.status===429){
        submissionError='Πολλές προσπάθειες εγγραφής. Δοκιμάστε αργότερα.';
      }else if(response.status===422){
        submissionError='Ελέγξτε τα στοιχεία και τις συγκαταθέσεις σας.';
      }else{
        submissionError='Η ασφαλής υποβολή δεν είναι διαθέσιμη. Κανένα στοιχείο δεν έχει καταχωριστεί. Δοκιμάστε αργότερα ή επικοινωνήστε με την Ακαδημία.';
      }
    }catch{
      submissionError='Δεν ήταν δυνατή η επικοινωνία με τον διακομιστή. Δεν υπάρχει επιβεβαίωση υποβολής.';
    }finally{pending=false;}
  }
</script>

<svelte:head><title>Έντυπο Εγγραφής | ΛΕΥΤΕΡΙΑ FC</title><meta name="robots" content="noindex" /></svelte:head>
<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups}>
  <div class="academy-registration" data-testid="registration-page">
    <div class="registration-wrap">
      {#if submitted}
        <div class="registration-card registration-success" data-testid="registration-success" role="status">
          <h1>Η Αίτηση Υποβλήθηκε</h1>
          <p>Η αίτηση καταχωρίστηκε προς έλεγχο από την Ακαδημία. Δεν αποτελεί αυτόματη εγγραφή αθλητή ή πληρωμή.</p>
          <p>Κωδικός παραλαβής: <strong>{receipt}</strong></p>
          <a class="public-button" href="/academy">Πίσω στην Ακαδημία <Icon name="arrow" size={17}/></a>
        </div>
      {:else}
        <header class="registration-heading">
          <p class="registration-badge">Ακαδημία Ποδοσφαίρου</p>
          <h1>Έντυπο <span>Εγγραφής</span></h1>
          <p>{data.editorialLead ?? 'Συμπληρώστε τη φόρμα για να αιτηθείτε την εγγραφή του αθλητή στην ακαδημία.'}</p>
        </header>
        <ol class="registration-steps" aria-label="Βήματα εγγραφής" data-testid="step-indicator">
          {#each steps as step,index (step.label)}
            <li aria-current={currentStep===index+1?'step':undefined} class:completed={currentStep>index+1}>
              <button type="button" class="registration-step-icon" disabled={pending||index+1>=currentStep}
                onclick={()=>{currentStep=index+1;errors={};}} aria-label={step.label} data-testid={`step-btn-${index+1}`}>
                <Icon name={step.icon} size={16}/>
              </button><span>{step.label}</span>
            </li>
          {/each}
        </ol>
        <section class="registration-card" data-testid="form-card">
          <form onsubmit={submit} novalidate={false} aria-busy={pending}>
            {#if currentStep===1}
              <div data-testid="step-1"><h2>Στοιχεία Παίκτη</h2><p>Συμπληρώστε τα βασικά στοιχεία του αθλητή.</p>
                <fieldset class="registration-fields" disabled={pending}>
                  <legend class="sr-only">Στοιχεία Παίκτη</legend>
                  <label>Όνομα <span>*</span><input bind:value={form.player_first_name} maxlength="100" required data-testid="player-first-name" placeholder="π.χ. Γιώργος"/>{#if errors.player_first_name}<small role="alert">{errors.player_first_name}</small>{/if}</label>
                  <label>Επώνυμο <span>*</span><input bind:value={form.player_last_name} maxlength="100" required data-testid="player-last-name" placeholder="π.χ. Παπαδόπουλος"/></label>
                  <label>Ημερομηνία Γέννησης <span>*</span><input type="date" bind:value={form.player_dob} required data-testid="player-dob"/></label>
                  <label>Φύλο <span>*</span><select bind:value={form.player_gender} required data-testid="player-gender"><option value="">Επιλέξτε...</option><option value="male">Αγόρι</option><option value="female">Κορίτσι</option></select></label>
                  <label class="registration-address">Διεύθυνση Κατοικίας <span>*</span><input bind:value={form.player_address} maxlength="200" required data-testid="player-address"/></label>
                  <label>Πόλη <span>*</span><input bind:value={form.player_city} maxlength="100" required data-testid="player-city"/></label>
                  <label>Τ.Κ.<input bind:value={form.player_postal_code} maxlength="20" data-testid="player-postal"/></label>
                  <h3 class="registration-full">Επίσημη στολή και υποχρεωτική εκτύπωση · €150</h3>
                  <label>Προτιμώμενος αριθμός φανέλας <span>*</span>
                    <input type="text" inputmode="numeric" pattern="[0-9][0-9]?" bind:value={form.preferred_number}
                      maxlength="2" required data-testid="player-preferred-number" placeholder="π.χ. 10"/>
                  </label>
                  <label>Προτιμώμενο όνομα εκτύπωσης <span>*</span>
                    <input bind:value={form.preferred_print_name} maxlength="32" required
                      data-testid="player-preferred-print-name" placeholder="π.χ. ΓΙΩΡΓΟΣ"/>
                  </label>
                  <p class="registration-full registration-note">Η επίσημη στολή κοστίζει €140 και η εκτύπωση ονόματος και αριθμού είναι <strong>υποχρεωτική (€10)</strong>. Ο αριθμός αποτελεί προτίμηση, όχι κράτηση. Εάν χρησιμοποιείται ήδη, ο υπεύθυνος τοποθεσίας θα επικοινωνήσει μαζί σας για τυχόν αλλαγή.</p>
                </fieldset>
                {#each additionalPlayers as child,i (i)}
                  <section class="registration-child" data-testid="additional-player">
                    <header class="registration-child-head">
                      <h3>Παιδί {i+2} · {child.player_first_name||'Νέος παίκτης'}</h3>
                      <button type="button" disabled={pending} onclick={()=>removePlayer(i)}
                        aria-label={'Αφαίρεση παιδιού '+(i+2)}>Αφαίρεση</button>
                    </header>
                    <fieldset class="registration-fields" disabled={pending}>
                      <legend class="sr-only">Στοιχεία παιδιού {i+2}</legend>
                      <label>Όνομα <span>*</span><input bind:value={child.player_first_name} maxlength="100" required data-testid="additional-first-name"/></label>
                      <label>Επώνυμο <span>*</span><input bind:value={child.player_last_name} maxlength="100" required/></label>
                      <label>Ημερομηνία γέννησης <span>*</span><input type="date" bind:value={child.player_dob} required/></label>
                      <label>Φύλο <span>*</span><select bind:value={child.player_gender} required><option value="">Επιλέξτε...</option><option value="male">Αγόρι</option><option value="female">Κορίτσι</option></select></label>
                      <label class="registration-full">Διεύθυνση κατοικίας <span>*</span><input bind:value={child.player_address} maxlength="200" required/></label>
                      <label>Πόλη <span>*</span><input bind:value={child.player_city} maxlength="100" required/></label>
                      <label>Τ.Κ.<input bind:value={child.player_postal_code} maxlength="20"/></label>
                      <h3 class="registration-full">Στολή €140 + υποχρεωτική εκτύπωση €10</h3>
                      <label>Προτιμώμενος αριθμός <span>*</span><input inputmode="numeric" pattern="[0-9][0-9]?" bind:value={child.preferred_number} maxlength="2" required/></label>
                      <label>Όνομα εκτύπωσης <span>*</span><input bind:value={child.preferred_print_name} maxlength="32" required/></label>
                      <p class="registration-full registration-note">Η επιλογή αριθμού μπορεί να χρειαστεί αλλαγή μετά τον έλεγχο του υπεύθυνου τοποθεσίας.</p>
                    </fieldset>
                  </section>
                {/each}
                {#if additionalPlayers.length<4}
                  <button type="button" class="registration-add-child" disabled={pending}
                    onclick={addPlayer} data-testid="add-player">+ Προσθήκη άλλου παιδιού</button>
                {/if}
                <p class="registration-family-note">Μία αίτηση, μία επικοινωνία γονέα και μία υπογραφή για {playerCount} {playerCount===1?'παιδί':'παιδιά'}. Κάθε παιδί διατηρεί τη δική του ιατρική δήλωση.</p>
              </div>
            {:else if currentStep===2}
              <div data-testid="step-2"><h2>Στοιχεία Γονέα / Κηδεμόνα</h2><p>Κύρια στοιχεία επικοινωνίας και υπεύθυνος έκτακτης ανάγκης.</p>
                <fieldset class="registration-fields" disabled={pending}>
                  <legend class="sr-only">Γονέας και επαφή έκτακτης ανάγκης</legend>
                  <label>Ονοματεπώνυμο <span>*</span><input bind:value={form.parent_name} maxlength="200" required data-testid="parent-name"/></label>
                  <label>Σχέση με τον Παίκτη <span>*</span><select bind:value={form.parent_relationship} required data-testid="parent-relationship"><option value="">Επιλέξτε...</option><option>Πατέρας</option><option>Μητέρα</option><option>Κηδεμόνας</option></select></label>
                  <label>Τηλέφωνο <span>*</span><input type="tel" bind:value={form.parent_phone} maxlength="32" required data-testid="parent-phone"/></label>
                  <label>Email <span>*</span><input type="email" bind:value={form.parent_email} maxlength="254" required data-testid="parent-email"/></label>
                  <h3 class="registration-full">Στοιχεία Έκτακτης Ανάγκης</h3>
                  <label>Ονοματεπώνυμο <span>*</span><input bind:value={form.emergency_name} maxlength="200" required data-testid="emergency-name"/></label>
                  <label>Τηλέφωνο <span>*</span><input type="tel" bind:value={form.emergency_phone} maxlength="32" required data-testid="emergency-phone"/></label>
                  <label class="registration-full">Σχέση με τον Παίκτη<input bind:value={form.emergency_relationship} maxlength="80" data-testid="emergency-relationship"/></label>
                </fieldset>
              </div>
            {:else if currentStep===3}
              <div data-testid="step-3"><h2>Ιατρικές Πληροφορίες ανά παιδί</h2><p>Δηλώστε ξεχωριστά τις απαραίτητες πληροφορίες υγείας κάθε παιδιού. Φυλάσσονται μόνο σε κρυπτογραφημένη μορφή.</p>
                <h3 class="registration-medical-child">Παιδί 1 · {form.player_first_name} {form.player_last_name}</h3>
                {#each [{key:'allergies',title:'Αλλεργίες'},{key:'conditions',title:'Ιατρικές Παθήσεις'},{key:'medication',title:'Φαρμακευτική Αγωγή'}] as item (item.key)}
                  <fieldset class="registration-choice-card" disabled={pending}>
                    <legend>{item.title}</legend>
                    {#if item.key==='allergies'}
                      <label><input type="radio" name="allergies" checked={form.has_allergies} onchange={()=>form.has_allergies=true}/> Ναι</label>
                      <label><input type="radio" name="allergies" checked={!form.has_allergies} onchange={()=>{form.has_allergies=false;form.allergies_details='';}}/> Όχι</label>
                      {#if form.has_allergies}<textarea bind:value={form.allergies_details} maxlength="1500" placeholder="Περιγράψτε τις αλλεργίες" data-testid="allergies-details"></textarea>{/if}
                    {:else if item.key==='conditions'}
                      <label><input type="radio" name="conditions" checked={form.has_conditions} onchange={()=>form.has_conditions=true}/> Ναι</label>
                      <label><input type="radio" name="conditions" checked={!form.has_conditions} onchange={()=>{form.has_conditions=false;form.conditions_details='';}}/> Όχι</label>
                      {#if form.has_conditions}<textarea bind:value={form.conditions_details} maxlength="1500" placeholder="Περιγράψτε τις παθήσεις" data-testid="conditions-details"></textarea>{/if}
                    {:else}
                      <label><input type="radio" name="medication" checked={form.has_medication} onchange={()=>form.has_medication=true}/> Ναι</label>
                      <label><input type="radio" name="medication" checked={!form.has_medication} onchange={()=>{form.has_medication=false;form.medication_details='';}}/> Όχι</label>
                      {#if form.has_medication}<textarea bind:value={form.medication_details} maxlength="1500" placeholder="Περιγράψτε τη φαρμακευτική αγωγή" data-testid="medication-details"></textarea>{/if}
                    {/if}
                  </fieldset>
                {/each}
                {#each additionalPlayers as child,i (i)}
                  <section class="registration-child" data-testid="additional-medical">
                    <h3 class="registration-medical-child">Παιδί {i+2} · {child.player_first_name} {child.player_last_name}</h3>
                    <fieldset class="registration-choice-card" disabled={pending}>
                      <legend>Αλλεργίες</legend>
                      <label><input type="radio" name={'child-'+i+'-allergies'} checked={child.has_allergies} onchange={()=>child.has_allergies=true}/> Ναι</label>
                      <label><input type="radio" name={'child-'+i+'-allergies'} checked={!child.has_allergies} onchange={()=>{child.has_allergies=false;child.allergies_details='';}}/> Όχι</label>
                      {#if child.has_allergies}<textarea bind:value={child.allergies_details} maxlength="1500" placeholder="Περιγράψτε τις αλλεργίες του παιδιού"></textarea>{/if}
                    </fieldset>
                    <fieldset class="registration-choice-card" disabled={pending}>
                      <legend>Ιατρικές παθήσεις</legend>
                      <label><input type="radio" name={'child-'+i+'-conditions'} checked={child.has_conditions} onchange={()=>child.has_conditions=true}/> Ναι</label>
                      <label><input type="radio" name={'child-'+i+'-conditions'} checked={!child.has_conditions} onchange={()=>{child.has_conditions=false;child.conditions_details='';}}/> Όχι</label>
                      {#if child.has_conditions}<textarea bind:value={child.conditions_details} maxlength="1500" placeholder="Περιγράψτε τις παθήσεις του παιδιού"></textarea>{/if}
                    </fieldset>
                    <fieldset class="registration-choice-card" disabled={pending}>
                      <legend>Φαρμακευτική αγωγή</legend>
                      <label><input type="radio" name={'child-'+i+'-medication'} checked={child.has_medication} onchange={()=>child.has_medication=true}/> Ναι</label>
                      <label><input type="radio" name={'child-'+i+'-medication'} checked={!child.has_medication} onchange={()=>{child.has_medication=false;child.medication_details='';}}/> Όχι</label>
                      {#if child.has_medication}<textarea bind:value={child.medication_details} maxlength="1500" placeholder="Περιγράψτε τη φαρμακευτική αγωγή του παιδιού"></textarea>{/if}
                    </fieldset>
                  </section>
                {/each}
              </div>
            {:else if currentStep===4}
              <div data-testid="step-4"><h2>Όροι & Συγκαταθέσεις</h2><p>Διαβάστε τις <a href="/terms" target="_blank" rel="noreferrer">προϋποθέσεις</a> και την <a href="/privacy" target="_blank" rel="noreferrer">πολιτική απορρήτου</a>.</p>
                <fieldset class="registration-consents" disabled={pending}><legend class="sr-only">Συγκαταθέσεις</legend>
                  <label><input type="checkbox" bind:checked={form.consent_participation} data-testid="consent-participation"/> Συμφωνώ με τη συμμετοχή του παιδιού στις δραστηριότητες της Ακαδημίας <span>*</span></label>
                  <label><input type="checkbox" bind:checked={form.consent_medical_auth} data-testid="consent-medical"/> Εξουσιοδοτώ την αναγκαία ιατρική φροντίδα σε περίπτωση έκτακτης ανάγκης <span>*</span></label>
                  <label><input type="checkbox" bind:checked={form.consent_gdpr} data-testid="consent-gdpr"/> Έχω διαβάσει την πολιτική προστασίας προσωπικών δεδομένων <span>*</span></label>
                  <fieldset class="registration-choice-card"><legend>Επιτρέπω τη χρήση φωτογραφίας/βίντεο του παιδιού; <span>*</span></legend>
                    <label><input type="radio" name="media" value="yes" bind:group={form.consent_media} data-testid="media-yes"/> Ναι</label>
                    <label><input type="radio" name="media" value="no" bind:group={form.consent_media} data-testid="media-no"/> Όχι</label>
                  </fieldset>
                  <label><input type="checkbox" bind:checked={form.consent_communications} data-testid="consent-comms"/> Επιθυμώ να λαμβάνω ενημερώσεις</label>
                  {#if form.consent_communications}<div class="registration-consent-channels"><label><input type="checkbox" bind:checked={form.comm_email}/> Email</label><label><input type="checkbox" bind:checked={form.comm_sms}/> SMS</label></div>{/if}
                  <label><input type="checkbox" bind:checked={form.consent_liability} data-testid="consent-liability"/> Έχω ενημερωθεί για τους κινδύνους των αθλητικών δραστηριοτήτων και τους σχετικούς όρους <span>*</span></label>
                  <label><input type="checkbox" bind:checked={form.consent_financial} data-testid="consent-financial"/> Έχω λάβει γνώση και αποδέχομαι τους ισχύοντες οικονομικούς όρους <span>*</span></label>
                  <p class="registration-notice">Η συγκατάθεση για φωτογραφίες και εμπορική επικοινωνία είναι προαιρετική. Η άρνησή τους δεν εμποδίζει την αίτηση.</p>
                </fieldset>
              </div>
            {:else}
              <div data-testid="step-5"><h2>Πληρωμή & Υπογραφή</h2><p>Η επιλογή πληρωμής αποτελεί προτίμηση· δεν γίνεται αυτόματη χρέωση με την υποβολή.</p>
                <section class="registration-costs" data-testid="registration-fee-summary" aria-label="Ανάλυση εκτιμώμενων χρεώσεων">
                  <h3>Ανάλυση χρεώσεων · {playerCount} {playerCount===1?'παιδί':'παιδιά'}</h3>
                  <div><span>Μηνιαία συνδρομή οικογένειας</span><strong>€{monthlyFee}</strong></div>
                  <div><span>Εφάπαξ εγγραφή · €20 × {playerCount}</span><strong>€{registrationFees}</strong></div>
                  <div><span>Επίσημη στολή · €140 × {playerCount}</span><strong>€{playerCount*140}</strong></div>
                  <div><span>Υποχρεωτική εκτύπωση · €10 × {playerCount}</span><strong>€{playerCount*10}</strong></div>
                  <div class="registration-total"><span>Αρχικό εκτιμώμενο σύνολο με τον 1ο μήνα</span><strong>€{initialTotal}</strong></div>
                  <p>Η εκτύπωση είναι υποχρεωτική για κάθε επίσημη στολή. Οι αριθμοί είναι προτιμήσεις υπό επιβεβαίωση από τον υπεύθυνο τοποθεσίας. Η υποβολή δεν αποτελεί τιμολόγιο ή πληρωμή.</p>
                </section>
                <fieldset class="registration-payment" disabled={pending}><legend>Επιθυμητός τρόπος πληρωμής</legend>
                  <label><input type="radio" value="cash" bind:group={form.payment_method} data-testid="payment-cash"/> Μετρητά</label>
                  <label><input type="radio" value="card" bind:group={form.payment_method} data-testid="payment-card"/> Κάρτα</label>
                  <label><input type="radio" value="transfer" bind:group={form.payment_method} data-testid="payment-transfer"/> Μεταφορά</label>
                </fieldset>
                <p>Δηλώνω ότι τα στοιχεία είναι αληθή και υπογράφω ως γονέας ή νόμιμος κηδεμόνας.</p>
                <label class="registration-signature-label" for="registration-signature">Υπογραφή Γονέα / Κηδεμόνα <span>*</span></label>
                <canvas id="registration-signature" bind:this={canvas} width="600" height="150" class="registration-signature"
                  onpointerdown={beginSignature} onpointermove={drawSignature} onpointerup={finishSignature}
                  onpointercancel={finishSignature} aria-label="Πεδίο υπογραφής" data-testid="signature-canvas"></canvas>
                <button type="button" class="registration-clear" onclick={clearSignature} disabled={pending} data-testid="signature-clear">Καθαρισμός υπογραφής</button>
                <label class="registration-signature-label">Ημερομηνία <span>*</span><input type="date" bind:value={form.signature_date} required data-testid="signature-date"/></label>
              </div>
            {/if}
            {#if Object.keys(errors).length}<p class="registration-errors" role="alert">Συμπληρώστε τα υποχρεωτικά πεδία: {Object.keys(errors).length} εκκρεμότητες.</p>{/if}
            {#if submissionError}<p class="registration-errors" role="alert" data-testid="registration-submit-error">{submissionError}</p>{/if}
            <nav class="registration-actions" aria-label="Μετακίνηση στη φόρμα">
              {#if currentStep===1}<a href="/academy">← Ακαδημία</a>{:else}<button type="button" disabled={pending} onclick={previous} data-testid="prev-step-btn">← Πίσω</button>{/if}
              {#if currentStep<5}<button type="button" class="public-button" disabled={pending} onclick={next} data-testid="next-step-btn">Επόμενο <Icon name="arrow" size={16}/></button>
              {:else}<button type="submit" class="public-button" disabled={pending} data-testid="submit-btn">{pending?'Υποβολή…':'Υποβολή Αίτησης'} <Icon name="arrow" size={16}/></button>{/if}
            </nav>
          </form>
        </section>
      {/if}
    </div>
  </div>
</PublicShell>
<style>
  .registration-card h2{font-size:clamp(23px,2vw,30px);margin:0 0 5px}
  .registration-card h3{font-size:22px;margin:8px 0}
  .registration-card>form>div>p{font-size:14px;color:#a1a1aa;margin:4px 0 22px}
  .registration-card :is(input[type=checkbox],input[type=radio]){width:18px;min-width:18px;min-height:18px;accent-color:#f6bb38}
  .registration-fields{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}
  .registration-full{grid-column:1/-1}
  .registration-fields label{display:block;min-width:0}
  .registration-fields :is(input,select){display:block}
  .registration-choice-card{margin:18px 0;padding:18px;border:1px solid #333;background:#0d0d0d;border-radius:7px;display:flex;flex-wrap:wrap;gap:16px}
  .registration-choice-card legend{font-weight:700;color:#fff;padding:0 8px}
  .registration-choice-card label,.registration-consents label,.registration-payment label{display:inline-flex;align-items:center;gap:10px;line-height:1.5;color:#ececec}
  .registration-choice-card textarea{width:100%;min-height:88px;padding:12px;border:1px solid #555;background:#101010;color:white}
  .registration-consents{display:grid;gap:19px;border:0;padding:0}
  .registration-consents>label{align-items:flex-start}
  .registration-consent-channels,.registration-payment{display:flex;gap:18px;flex-wrap:wrap}
  .registration-notice{font-size:13px;color:#b1b1b1}
  .registration-card a:not(.public-button){text-decoration:underline;text-underline-offset:3px;color:#ffcb58}
  .registration-signature-label{display:block;margin:18px 0 8px}
  .registration-signature{display:block;width:100%;height:150px;max-width:100%;background:#0d0d0d;border:1px solid #535353;touch-action:none;cursor:crosshair;border-radius:7px}
  .registration-clear{color:#f6bb38;font-size:13px;margin-top:8px}
  .registration-errors{border-left:3px solid #eaaa38;padding:12px;margin:18px 0;background:#291e0d;color:#ffe5b9}
  .registration-actions{display:flex;justify-content:space-between;align-items:center;gap:12px;margin-top:28px;padding-top:20px;border-top:1px solid #313131}
  .registration-actions button:not(.public-button){color:#eee;min-height:44px}
  .registration-step-icon{cursor:pointer;color:inherit}
  .registration-step-icon:disabled{cursor:default}
  .registration-steps .completed .registration-step-icon{border-color:#f6bb38;color:#f6bb38}
  .registration-child{margin:22px 0;border:1px solid #49402a;border-radius:12px;padding:clamp(16px,3vw,26px);background:linear-gradient(145deg,#171510,#0e0e0e)}
  .registration-child-head{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:18px}
  .registration-child-head h3{margin:0}
  .registration-child-head button{color:#f6bb38;padding:10px 12px;min-height:44px;border:1px solid #77602f;border-radius:6px}
  .registration-add-child{display:flex;align-items:center;justify-content:center;gap:8px;min-height:52px;width:100%;margin:18px 0;padding:12px 18px;border:1px dashed #f6bb38;border-radius:9px;background:#241d0d;color:#f6bb38;font-weight:800;cursor:pointer}
  .registration-add-child:hover,.registration-add-child:focus-visible{background:#35270e;outline-offset:3px}
  .registration-note,.registration-family-note{font-size:13px;line-height:1.65;color:#d4cbb4}
  .registration-medical-child{margin:20px 0 12px;padding:12px 16px;border-left:4px solid #f6bb38;background:#191710}
  .registration-costs{padding:clamp(16px,3vw,26px);background:#121212;border:1px solid #59451e;border-radius:12px;margin:24px 0}
  .registration-costs h3{margin:0 0 18px}
  .registration-costs>div{display:flex;justify-content:space-between;align-items:flex-start;gap:16px;padding:11px 0;border-bottom:1px solid #303030}
  .registration-costs strong{font-variant-numeric:tabular-nums;white-space:nowrap}
  .registration-costs .registration-total{border-top:2px solid #f6bb38;border-bottom:0;margin-top:10px;font-weight:800}
  .registration-costs .registration-total strong{color:#f6bb38;font-size:24px}
  .registration-costs>p{margin:15px 0 0;color:#bfbfbf;font-size:13px;line-height:1.6}
  .registration-card input:focus-visible,.registration-card select:focus-visible,
  .registration-card textarea:focus-visible{outline:2px solid #f6bb38;outline-offset:2px}
  .registration-success{text-align:center;margin-top:40px}
  .registration-success h1{font-size:36px}
  .registration-success p{margin:16px auto;max-width:45ch}
  @media(max-width:600px){.registration-fields{grid-template-columns:1fr}.registration-actions .public-button{font-size:15px}}
</style>
