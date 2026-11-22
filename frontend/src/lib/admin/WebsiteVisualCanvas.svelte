<script lang="ts">
  import {DEFAULT_BLOCK_LAYOUT,DEFAULT_PAGE_LAYOUT,validBlockLayout,validPageLayout,type WebsiteBlock,type WebsiteBlockLayout,type WebsitePageLayout} from '../public/website-pages.ts';
  type Props={
    blocks:WebsiteBlock[];
    editable:boolean;
    locked:boolean;
    busy:boolean;
    maxBlocks?:number;
    pageLayout?:WebsitePageLayout;
    onLayout?:(index:number,layout:WebsiteBlockLayout)=>void;
    onLevel?:(index:number,level:2|3|4|5|6)=>void;
    onEdit:(index:number,text:string)=>void;
    onMove:(from:number,to:number)=>void;
    onInsert:(index:number,type:WebsiteBlock['type'])=>void;
    onSelect:(index:number)=>void;
  };
  let {blocks,editable,locked,busy,maxBlocks=40,pageLayout,onLayout,onLevel,onEdit,onMove,onInsert,onSelect}:Props=$props();
  const resolvedPage=$derived(validPageLayout(pageLayout)?pageLayout??DEFAULT_PAGE_LAYOUT:DEFAULT_PAGE_LAYOUT);
  const blockLayout=(block:WebsiteBlock)=>validBlockLayout(block.layout)&&block.layout?block.layout:DEFAULT_BLOCK_LAYOUT;
  function setLayout(index:number,key:keyof WebsiteBlockLayout,value:number|string){
    const block=blocks[index];if(!block||!onLayout||!editable||locked||busy)return;
    const next={...blockLayout(block),[key]:value} as WebsiteBlockLayout;
    if(validBlockLayout(next))onLayout(index,next);
  }
  let selected=$state<number|null>(null);
  let dragged=$state<number|null>(null);
  let hover=$state<number|null>(null);
  const types: {kind:WebsiteBlock['type'];label:string}[]=[
    {kind:'heading',label:'Τίτλος'},
    {kind:'paragraph',label:'Κείμενο'},
    {kind:'image',label:'Εικόνα'},
    {kind:'button',label:'Κουμπί'}
  ];
  function choose(index:number){
    selected=index;onSelect(index);
  }
  function validImage(value:string|null):string|null{
    if(!value||value.length>2048||value.includes('\\'))return null;
    if(value.startsWith('/')&&!value.startsWith('//'))return value;
    try{const parsed=new URL(value);return parsed.protocol==='https:'?value:null;}catch{return null;}
  }
  function start(event:DragEvent,index:number){
    if(!editable||locked||busy){event.preventDefault();return;}
    dragged=index;
    if(event.dataTransfer){
      event.dataTransfer.effectAllowed='move';
      event.dataTransfer.setData('text/plain','lf-cms-block');
    }
  }
  function over(event:DragEvent,index:number){
    if(dragged===null||locked||busy||!editable)return;
    event.preventDefault();hover=index;
    if(event.dataTransfer)event.dataTransfer.dropEffect='move';
  }
  function drop(event:DragEvent,index:number){
    if(dragged===null||locked||busy||!editable)return;
    event.preventDefault();
    const from=dragged;dragged=null;hover=null;
    if(from!==index){onMove(from,index);selected=index;onSelect(index);}
  }
  function end(){dragged=null;hover=null;}
</script>

<div class="visual-canvas" data-testid="website-visual-canvas"
  style={`--cms-max-width:${resolvedPage.max_width}px;--cms-gap:${resolvedPage.gap}px;--cms-side:${resolvedPage.side_padding}px`}>
  <div class="visual-canvas-top"><strong>ΚΑΜΒΑΣ ΣΕΛΙΔΑΣ · LIVE</strong>
    <span>Κάντε κλικ σε κείμενο για άμεση επεξεργασία · σύρετε μια ενότητα για αλλαγή σειράς</span>
  </div>
  {#each blocks as block,index (block)}
    <div class="visual-slot" data-width={blockLayout(block).width}
      style={`--cms-min-height:${blockLayout(block).min_height}px;--cms-mt:${blockLayout(block).margin_top}px;--cms-mb:${blockLayout(block).margin_bottom}px`}
      class:selected={selected===index}
      class:over={hover===index&&dragged!==index}
      ondragover={e=>over(e,index)} ondrop={e=>drop(e,index)}
      data-testid="website-visual-section">
      {#if editable&&!locked}
        <div class="visual-insert-row">
          <span>+ Ενότητα εδώ</span>
          {#each types as kind (kind.kind)}
            <button type="button" disabled={busy||blocks.length>=maxBlocks}
              onclick={()=>onInsert(index,kind.kind)}
              aria-label={'Προσθήκη '+kind.label+' πριν από την ενότητα '+(index+1)}>+ {kind.label}</button>
          {/each}
        </div>
      {/if}
      <div class="visual-block">
        <div class="visual-tools">
          <button type="button" class="visual-select"
            aria-pressed={selected===index} onclick={()=>choose(index)}>
            {block.type==='heading'?'Τίτλος':block.type==='paragraph'?'Κείμενο':block.type==='image'?'Εικόνα':'Κουμπί'} · {index+1}
          </button>
          {#if editable&&!locked}
            <button type="button" draggable={!busy} class="visual-grip"
              disabled={busy} ondragstart={e=>start(e,index)} ondragend={end}
              aria-label={'Σύρετε την ενότητα '+(index+1)} title="Σύρετε για μετακίνηση">⠿ Μετακίνηση</button>
          {/if}
        </div>
        {#if editable&&!locked&&!busy&&selected===index&&block.type==='heading'&&onLevel}
          <label class="visual-heading-level">Σημασιολογικό επίπεδο τίτλου
            <select data-testid="canvas-heading-level" value={block.level??2}
              onchange={e=>onLevel(index,Number(e.currentTarget.value) as 2|3|4|5|6)}>
              {#each [2,3,4,5,6] as level}<option value={level}>H{level}</option>{/each}
            </select>
          </label>
        {/if}
        {#if editable&&!locked&&!busy&&selected===index&&onLayout}
          <div class="visual-layout-controls" data-testid="cms-block-layout-controls">
            <label>Πλάτος
              <select value={blockLayout(block).width}
                onchange={e=>setLayout(index,'width',e.currentTarget.value)}>
                <option value="full">1/1</option><option value="half">1/2</option><option value="third">1/3</option>
              </select>
            </label>
            <label>Ελάχιστο ύψος
              <select value={blockLayout(block).min_height}
                onchange={e=>setLayout(index,'min_height',Number(e.currentTarget.value))}>
                {#each [0,120,240,360,480] as size}<option value={size}>{size===0?'Αυτόματο':size+' px'}</option>{/each}
              </select>
            </label>
            <label>Πάνω περιθώριο
              <select value={blockLayout(block).margin_top}
                onchange={e=>setLayout(index,'margin_top',Number(e.currentTarget.value))}>
                {#each [0,8,16,24,32,48,64] as size}<option value={size}>{size} px</option>{/each}
              </select>
            </label>
            <label>Κάτω περιθώριο
              <select value={blockLayout(block).margin_bottom}
                onchange={e=>setLayout(index,'margin_bottom',Number(e.currentTarget.value))}>
                {#each [0,8,16,24,32,48,64] as size}<option value={size}>{size} px</option>{/each}
              </select>
            </label>
          </div>
        {/if}
        {#if editable&&!busy&&selected===index&&(block.type==='paragraph'||block.type==='heading')}
          <label class="visual-edit-label" for={'visual-text-'+index}>Επεξεργασία κειμένου πάνω στον καμβά
            <textarea id={'visual-text-'+index} rows={block.type==='heading'?2:5}
              maxlength="5000" value={block.text}
              oninput={e=>onEdit(index,e.currentTarget.value)} data-testid="website-visual-inline-text"/>
          </label>
        {:else if block.type==='heading'}
          <button type="button" class="visual-heading" onclick={()=>choose(index)}>{block.text}</button>
        {:else if block.type==='paragraph'}
          <button type="button" class="visual-paragraph" onclick={()=>choose(index)}>{block.text}</button>
        {:else if block.type==='image'}
          {#if validImage(block.url)}
            <button type="button" class="visual-image" onclick={()=>choose(index)}
              aria-label={'Επεξεργασία εικόνας: '+block.text}>
              <img src={validImage(block.url)??''} alt={block.text} loading="lazy"/>
            </button>
          {:else}
            <button type="button" class="visual-placeholder" onclick={()=>choose(index)}>Επιλέξτε εικόνα από την βιβλιοθήκη</button>
          {/if}
        {:else}
          <button type="button" class="visual-button" onclick={()=>choose(index)}>{block.text||'Κουμπί'}</button>
        {/if}
      </div>
    </div>
  {/each}
  {#if editable&&!locked}
    <div class="visual-insert-row last">
      <span>+ Προσθήκη στο τέλος</span>
      {#each types as kind (kind.kind)}
        <button type="button" disabled={busy||blocks.length>=maxBlocks}
          onclick={()=>onInsert(blocks.length,kind.kind)}>+ {kind.label}</button>
      {/each}
    </div>
  {/if}
</div>

<style>
  .visual-canvas{background:#090b0c;border:1px solid #4a4b45;border-radius:10px;
    padding:clamp(12px,2vw,20px);display:grid;grid-template-columns:repeat(6,minmax(0,1fr));gap:var(--cms-gap);max-width:var(--cms-max-width);padding-inline:var(--cms-side);margin-inline:auto}
  .visual-canvas-top,.visual-insert-row.last{grid-column:1/-1}
  .visual-slot{grid-column:span 6;min-height:var(--cms-min-height);margin-top:var(--cms-mt);margin-bottom:var(--cms-mb)}
  @media(min-width:768px){.visual-slot[data-width="half"]{grid-column:span 3}.visual-slot[data-width="third"]{grid-column:span 2}}
  @media(max-width:767px){.visual-canvas{grid-template-columns:minmax(0,1fr)}.visual-slot{grid-column:span 1}}
  .visual-layout-controls{display:grid;grid-template-columns:repeat(auto-fit,minmax(120px,1fr));gap:9px}
  .visual-layout-controls label{display:grid;gap:5px;font-size:11px;color:#e9c26c}
  .visual-layout-controls select{width:100%;min-height:36px;border:1px solid #766341;background:#161a1c;color:#fff;border-radius:5px;padding:5px}
  .visual-canvas-top{display:flex;align-items:center;justify-content:space-between;
    gap:8px;flex-wrap:wrap;margin-bottom:8px;color:#e7bd68;font-size:11px}
  .visual-canvas-top span{color:#a6adb1;font-size:11px}
  .visual-slot{border:1px solid transparent;border-radius:9px;min-width:0}
  .visual-slot.selected{border-color:#c39336;background:#251e1040}
  .visual-slot.over{border:2px dashed #d7ad55}
  .visual-block{padding:16px;min-width:0;display:grid;gap:10px}
  .visual-tools{display:flex;gap:8px;align-items:center;justify-content:space-between}
  .visual-tools button{background:#23282a;color:#eee5ce;border:1px solid #5f5a44;
    border-radius:5px;min-height:38px;padding:7px 10px;font-size:12px}
  .visual-grip{cursor:grab}
  .visual-heading,.visual-paragraph,.visual-image{border:0;background:transparent;
    color:#f9f9f4;text-align:left;padding:5px;min-width:0;white-space:pre-wrap;
    overflow-wrap:anywhere;cursor:text}
  .visual-heading{font:800 clamp(24px,3.5vw,48px)/1.14 'Fira Sans Extra Condensed',sans-serif}
  .visual-paragraph{font:400 15px/1.65 'Fira Sans',sans-serif}
  .visual-image img{max-width:100%;max-height:440px;object-fit:contain;border-radius:5px}
  .visual-button{justify-self:start;min-height:44px;padding:10px 18px;
    background:#f6bb38;color:#101112;border:0;border-radius:6px;font-weight:750}
  .visual-placeholder{padding:24px;border:1px dashed #746140;background:#1a1d1d;
    color:#e8ce9d;cursor:pointer}
  .visual-edit-label{display:grid;gap:8px;font-size:12px;color:#f9d17b}
  .visual-edit-label textarea{width:100%;min-height:85px;padding:10px;resize:vertical;
    border:2px solid #e7bb59!important;border-radius:6px;background:#15191b;color:#fff;
    font-size:15px;line-height:1.5}
  .visual-insert-row{display:flex;align-items:center;flex-wrap:wrap;gap:6px;
    padding:5px;border-top:1px dashed #454646;color:#a8adac;font-size:10px}
  .visual-insert-row span{margin-right:auto}
  .visual-insert-row button{font-size:11px;padding:5px 9px;min-height:32px;
    border:1px solid #534e3b;border-radius:5px;background:#171c1d;color:#f7d389;cursor:pointer}
  .visual-insert-row.last{margin-top:10px}
  .visual-canvas button:focus-visible{outline:2px solid #ffce68;outline-offset:2px}
  .visual-canvas button:disabled{opacity:.5;cursor:not-allowed}
</style>
