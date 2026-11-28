<script lang="ts">
  import {DEFAULT_GLOBAL_DESIGN,DEFAULT_GLOBAL_LAYOUT,GLOBAL_LAYOUT_CHOICES,parseGlobalDesignDraft,globalDesignContrastChecks,TEXT_ROLES,
    type GlobalDesign,type GlobalDesignLayout,type TextRole,type TextStyle} from '../public/global-design.ts';
  let {value,onChange,disabled=false}:{value:string;onChange:(text:string)=>void;disabled?:boolean}=$props();
  const parsedDraft=$derived(parseGlobalDesignDraft(value));
  const theme=$derived(parsedDraft??DEFAULT_GLOBAL_DESIGN);
  const contrastChecks=$derived(globalDesignContrastChecks(theme));
  const contrastFailures=$derived(contrastChecks.filter(check=>!check.passes));
  let previewMode=$state<'desktop'|'mobile'>('desktop');
  const paletteLabels={background:'Φόντο',foreground:'Κείμενο',accent:'Έμφαση',surface:'Κάρτες',muted:'Δευτερεύον κείμενο',border:'Περιγράμματα',link:'Σύνδεσμοι'};
  const previewStyle=(role:TextRole)=>'font-family:'+previewFonts[theme.text[role].family]+';font-size:'+theme.text[role][previewMode]+'px;font-weight:'+theme.text[role].weight+';line-height:'+theme.text[role].line_height;
  const resolvedLayout=$derived({...DEFAULT_GLOBAL_LAYOUT,...theme.layout});
  const previewFonts={condensed:"'Fira Sans Extra Condensed',sans-serif",sans:"'Fira Sans',Arial,sans-serif",serif:'Georgia,serif'} as const;
  const titles:Record<TextRole,string>={h1:'H1 · Τίτλος σελίδας',h2:'H2 · Ενότητα',h3:'H3 · Υποενότητα',
    h4:'H4',h5:'H5',h6:'H6',p:'Παράγραφος',button:'Κουμπιά',nav:'Πλοήγηση',caption:'Μικρό κείμενο',label:'Ετικέτες',card:'Κάρτες'};
  function updateRole(role:TextRole,key:keyof TextStyle,value:string|number){
    const next:GlobalDesign=structuredClone(theme);
    (next.text[role] as unknown as Record<string,string|number>)[key]=value;
    const encoded=JSON.stringify(next);
    if(parseGlobalDesignDraft(encoded))onChange(encoded);
  }
  const layoutLabels:Record<keyof GlobalDesignLayout,string>={
    gutter:'Οριζόντιο περιθώριο ενοτήτων',
    section_gap:'Απόσταση ενοτήτων',
    card_gap:'Απόσταση καρτών',
    card_radius:'Στρογγύλεμα καρτών',
    button_radius:'Στρογγύλεμα κουμπιών',
    motto_height:'Ύψος ζώνης ONE TEAM ● ONE DREAM',
    page_max_width:'Προεπιλεγμένο μέγιστο πλάτος σελίδας',
    page_gap:'Προεπιλεγμένο κενό μεταξύ blocks',
    page_side_padding:'Προεπιλεγμένο πλευρικό περιθώριο',
    page_margin_top:'Προεπιλεγμένο πάνω περιθώριο σελίδας',
    page_margin_bottom:'Προεπιλεγμένο κάτω περιθώριο σελίδας'
  };
  function updateLayout(key:keyof GlobalDesignLayout,value:number){
    const next:GlobalDesign=structuredClone(theme);
    next.layout={...DEFAULT_GLOBAL_LAYOUT,...next.layout,[key]:value};
    onChange(JSON.stringify(next));
  }
  function updateColor(key:keyof GlobalDesign['colors'],value:string){
    const next:GlobalDesign=structuredClone(theme);
    next.colors[key]=value;
    onChange(JSON.stringify(next));
  }
</script>
<section class="design-editor" data-testid="global-design-editor" aria-label="Γενική τυπογραφία και χρώματα">
  <h3>Τυπογραφία · πραγματικά H1–H6</h3>
  <p>Οι επιλογές επηρεάζουν τα αντίστοιχα σημασιολογικά HTML στοιχεία. Οι αλλαγές παραμένουν πρόχειρο μέχρι να εγκριθούν και να δημοσιευτούν.</p>
  {#each TEXT_ROLES as role (role)}
    <fieldset class="design-row">
      <legend>{titles[role]}</legend>
      <div class="design-inputs">
        <label>Γραμματοσειρά
          <select disabled={disabled} value={theme.text[role].family}
            onchange={e=>updateRole(role,'family',e.currentTarget.value)}>
            <option value="condensed">Condensed</option><option value="sans">Sans</option><option value="serif">Serif</option>
          </select>
        </label>
        <label>Desktop px
          <input type="number" min="12" max="80" step="1" disabled={disabled}
            value={theme.text[role].desktop} onchange={e=>updateRole(role,'desktop',Number(e.currentTarget.value))}/>
        </label>
        <label>Mobile px
          <input type="number" min="12" max="80" step="1" disabled={disabled}
            value={theme.text[role].mobile} onchange={e=>updateRole(role,'mobile',Number(e.currentTarget.value))}/>
        </label>
        <label>Βάρος
          <select disabled={disabled} value={theme.text[role].weight}
            onchange={e=>updateRole(role,'weight',Number(e.currentTarget.value))}>
            {#each [400,500,600,700,800] as weight}<option value={weight}>{weight}</option>{/each}
          </select>
        </label>
        <label>Ύψος γραμμής
          <select disabled={disabled} value={theme.text[role].line_height}
            onchange={e=>updateRole(role,'line_height',Number(e.currentTarget.value))}>
            {#each [1,1.15,1.2,1.35,1.5,1.6,1.8] as line}<option value={line}>{line}</option>{/each}
          </select>
        </label>
      </div>
    </fieldset>
  {/each}
  <h3>Χρωματική παλέτα</h3>
  <div class="design-inputs">
    {#each ['background','foreground','accent','surface','muted','border','link'] as key (key)}
      <label>{key==='background'?'Φόντο':key==='foreground'?'Κείμενο':key==='accent'?'Χρυσή έμφαση':key==='surface'?'Κάρτες / επιφάνειες':key==='muted'?'Δευτερεύον κείμενο':key==='border'?'Περιγράμματα':'Σύνδεσμοι'}
        <input type="color" disabled={disabled} value={theme.colors[key as keyof GlobalDesign['colors']]}
          onchange={e=>updateColor(key as keyof GlobalDesign['colors'],e.currentTarget.value)}/>
      </label>
    {/each}
  </div>
  {#if !parsedDraft}<p role="alert">Το πρόχειρο δεν έχει έγκυρη δομή. Ελέγξτε τις τιμές πριν αποθηκεύσετε.</p>{/if}
  {#if contrastFailures.length}
    <section class="contrast-review" role="status" data-testid="global-design-contrast-review">
      <strong>Η παλέτα δεν είναι ακόμη κατάλληλη για δημοσίευση.</strong>
      <p>Διορθώστε τους παρακάτω συνδυασμούς. Οι υπόλοιπες επιλογές του πρόχειρου διατηρούνται.</p>
      <ul>{#each contrastFailures as check (check.background+':'+check.foreground)}
        <li>{paletteLabels[check.background]} / {paletteLabels[check.foreground]}: {check.ratio.toFixed(2)}:1 · απαιτείται {check.minimum}:1</li>
      {/each}</ul>
    </section>
  {:else}
    <p role="status" data-testid="global-design-contrast-valid">Οι απαιτούμενοι συνδυασμοί χρωμάτων έχουν επαρκή αντίθεση.</p>
  {/if}
  <h3>Γενική διάταξη · αποστάσεις &amp; γωνίες</h3>
  <p>Οι τιμές είναι κοινές προεπιλογές για τις δημόσιες σελίδες του οργανισμού. Μπορείτε να αλλάξετε ένα πεδίο σε συγκεκριμένη σελίδα, χωρίς να αλλάξουν οι υπόλοιπες. Η διάταξη των blocks παραμένει ανεξάρτητη.</p>
  <div class="design-inputs" data-testid="global-design-layout-controls">
    {#each Object.keys(GLOBAL_LAYOUT_CHOICES) as key (key)}
      <label>{layoutLabels[key as keyof GlobalDesignLayout]}
        <select disabled={disabled} value={resolvedLayout[key as keyof GlobalDesignLayout]}
          onchange={e=>updateLayout(key as keyof GlobalDesignLayout,Number(e.currentTarget.value))}>
          {#each GLOBAL_LAYOUT_CHOICES[key as keyof GlobalDesignLayout] as option (option)}
            <option value={option}>{option}px</option>
          {/each}
        </select>
      </label>
    {/each}
  </div>
  <h3>Προεπισκόπηση πρόχειρου</h3>
  <div class="preview-switch" role="group" aria-label="Μέγεθος προεπισκόπησης">
    <button type="button" class="admin-secondary" aria-pressed={previewMode==='desktop'} onclick={()=>previewMode='desktop'}>Desktop</button>
    <button type="button" class="admin-secondary" aria-pressed={previewMode==='mobile'} onclick={()=>previewMode='mobile'}>Mobile</button>
  </div>
  <div class="design-preview" data-testid="global-design-responsive-preview" class:mobile={previewMode==='mobile'} style={'background:'+theme.colors.background+';color:'+theme.colors.foreground+';border-radius:'+resolvedLayout.card_radius+'px;gap:'+resolvedLayout.section_gap+'px;padding:'+resolvedLayout.page_side_padding+'px;--preview-h1-desktop:'+theme.text.h1.desktop+'px;--preview-h1-mobile:'+theme.text.h1.mobile+'px;--preview-p-desktop:'+theme.text.p.desktop+'px;--preview-p-mobile:'+theme.text.p.mobile+'px'}>
    <span style={'color:'+theme.colors.accent+';'+previewStyle('caption')}>LEFTERIA FC · LIVE DRAFT PREVIEW</span>
    <div style={previewStyle('h1')}>Η ΟΜΑΔΑ ΜΑΣ</div>
    <p style={previewStyle('p')}>Αθλητές, προπονήσεις, αγώνες και ανάπτυξη.</p>
    <div class="preview-card" style={'background:'+theme.colors.surface+';border-color:'+theme.colors.border+';border-radius:'+resolvedLayout.card_radius+'px;gap:'+resolvedLayout.card_gap+'px;padding:'+resolvedLayout.card_gap+'px'}>
      <div style={previewStyle('h2')}>Ακαδημία</div>
      <div style={previewStyle('card')}>ONE TEAM ● ONE DREAM</div>
      <span style={'color:'+theme.colors.muted+';'+previewStyle('caption')}>Προπονήσεις &amp; αγώνες</span>
      <span style={'color:'+theme.colors.link+';'+previewStyle('nav')}>Περισσότερα ›</span>
      <span class="preview-button" style={'background:'+theme.colors.accent+';color:'+theme.colors.background+';border-radius:'+resolvedLayout.button_radius+'px;'+previewStyle('button')}>Εγγραφή</span>
    </div>
  </div>
</section>
<style>
.design-editor{display:grid;gap:16px;padding:14px;border:1px solid #56472a;border-radius:10px}
.design-row{border:1px solid #424242;border-radius:7px;padding:10px;min-width:0}
.design-row legend{color:#f6bb38;padding-inline:7px;font-size:13px}
.design-inputs{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:10px}
.design-inputs label{display:grid;gap:5px;font-size:12px}
.design-inputs select,.design-inputs input{width:100%;max-width:100%;min-width:0;min-height:40px;background:#191919;color:#fff;border:1px solid #777;border-radius:5px;padding:5px}
.design-preview{display:grid;overflow:hidden;overflow-wrap:anywhere;box-sizing:border-box;min-width:0}
.design-preview.mobile{width:min(100%,390px);justify-self:center}
.design-preview p{margin:0}
.preview-card{display:grid;border:1px solid;min-width:0}
.preview-button{display:inline-block;padding:8px 12px;justify-self:start}
.preview-switch{display:flex;gap:8px;flex-wrap:wrap}
.preview-switch button[aria-pressed=true]{border-color:var(--admin-line);text-decoration:underline}
.contrast-review{display:grid;gap:6px;border:1px solid var(--admin-line);padding:12px;border-radius:8px}
.contrast-review p,.contrast-review ul{margin:0}
</style>
