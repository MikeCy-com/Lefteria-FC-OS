<script lang="ts">
  import {DEFAULT_GLOBAL_DESIGN,DEFAULT_GLOBAL_LAYOUT,GLOBAL_LAYOUT_CHOICES,parseGlobalDesign,TEXT_ROLES,
    type GlobalDesign,type GlobalDesignLayout,type TextRole,type TextStyle} from '../public/global-design.ts';
  let {value,onChange,disabled=false}:{value:string;onChange:(text:string)=>void;disabled?:boolean}=$props();
  const theme=$derived(parseGlobalDesign(value)??DEFAULT_GLOBAL_DESIGN);
  const titles:Record<TextRole,string>={h1:'H1 · Τίτλος σελίδας',h2:'H2 · Ενότητα',h3:'H3 · Υποενότητα',
    h4:'H4',h5:'H5',h6:'H6',p:'Παράγραφος',button:'Κουμπιά',nav:'Πλοήγηση',caption:'Μικρό κείμενο',label:'Ετικέτες',card:'Κάρτες'};
  function updateRole(role:TextRole,key:keyof TextStyle,value:string|number){
    const next:GlobalDesign=structuredClone(theme);
    (next.text[role] as unknown as Record<string,string|number>)[key]=value;
    onChange(JSON.stringify(next));
  }
  const layoutLabels:Record<keyof GlobalDesignLayout,string>={
    gutter:'Οριζόντιο περιθώριο ενοτήτων',
    section_gap:'Απόσταση ενοτήτων',
    card_gap:'Απόσταση καρτών',
    card_radius:'Στρογγύλεμα καρτών',
    button_radius:'Στρογγύλεμα κουμπιών',
    motto_height:'Ύψος ζώνης ONE TEAM ● ONE DREAM'
  };
  function updateLayout(key:keyof GlobalDesignLayout,value:number){
    const next:GlobalDesign=structuredClone(theme);
    next.layout={...(next.layout??DEFAULT_GLOBAL_LAYOUT),[key]:value};
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
  <h3>Γενική διάταξη · αποστάσεις &amp; γωνίες</h3>
  <p>Οι τιμές εφαρμόζονται σε όλες τις δημόσιες σελίδες του αντίστοιχου οργανισμού. Η διάταξη κάθε μπλοκ στο WYSIWYG παραμένει ανεξάρτητη.</p>
  <div class="design-inputs" data-testid="global-design-layout-controls">
    {#each Object.keys(GLOBAL_LAYOUT_CHOICES) as key (key)}
      <label>{layoutLabels[key as keyof GlobalDesignLayout]}
        <select disabled={disabled} value={(theme.layout??DEFAULT_GLOBAL_LAYOUT)[key as keyof GlobalDesignLayout]}
          onchange={e=>updateLayout(key as keyof GlobalDesignLayout,Number(e.currentTarget.value))}>
          {#each GLOBAL_LAYOUT_CHOICES[key as keyof GlobalDesignLayout] as option (option)}
            <option value={option}>{option}px</option>
          {/each}
        </select>
      </label>
    {/each}
  </div>
  <div class="design-preview" style={'background:'+theme.colors.background+';color:'+theme.colors.foreground+';border-radius:'+(theme.layout??DEFAULT_GLOBAL_LAYOUT).card_radius+'px'}>
    <span style={'color:'+theme.colors.accent}>LEFTERIA FC · LIVE DRAFT PREVIEW</span>
    <strong style={'font-size:'+theme.text.h1.desktop+'px;line-height:'+theme.text.h1.line_height}>Η ΟΜΑΔΑ ΜΑΣ</strong>
    <p>Αθλητές, προπονήσεις, αγώνες και ανάπτυξη.</p>
  </div>
</section>
<style>
.design-editor{display:grid;gap:16px;padding:14px;border:1px solid #56472a;border-radius:10px}
.design-row{border:1px solid #424242;border-radius:7px;padding:10px;min-width:0}
.design-row legend{color:#f6bb38;padding-inline:7px;font-size:13px}
.design-inputs{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:10px}
.design-inputs label{display:grid;gap:5px;font-size:12px}
.design-inputs select,.design-inputs input{width:100%;max-width:100%;min-width:0;min-height:40px;background:#191919;color:#fff;border:1px solid #777;border-radius:5px;padding:5px}
.design-preview{padding:20px;border-radius:8px;display:grid;gap:12px;overflow:hidden}
.design-preview strong{overflow-wrap:anywhere}
@media(max-width:767px){.design-preview strong{font-size:clamp(18px,8vw,36px)!important}}
</style>
