<script lang="ts">
  // A non-transactional basket preview. Server checkout must re-price all lines.
  type Variant={id:string;name:string;price_cents:number};
  type Product={id:string;name:string;active:boolean;category_id:string;variants:Variant[]};
  type Category={id:string;active:boolean};
  let {products,categories,organization}:{products:Product[];categories:Category[];organization:string}=$props();
  type Line={productId:string;variantId:string;quantity:number};
  let lines=$state<Line[]>([]);
  let chosenProduct=$state('');
  let chosenVariant=$state('');
  let quantity=$state(1);
  let notice=$state('');
  const sellable=$derived(products.filter(p=>p.active&&categories.some(c=>c.id===p.category_id&&c.active)));
  const selectedProduct=$derived(sellable.find(p=>p.id===chosenProduct));
  const selectedVariant=$derived(selectedProduct?.variants.find(v=>v.id===chosenVariant));
  const priced=$derived(lines.map(line=>{
    const product=sellable.find(p=>p.id===line.productId);
    const variant=product?.variants.find(v=>v.id===line.variantId);
    return product&&variant&&Number.isSafeInteger(variant.price_cents)&&variant.price_cents>0&&
      Number.isSafeInteger(line.quantity)&&line.quantity>=1&&line.quantity<=99
      ?{...line,name:product.name,variantName:variant.name,unit:variant.price_cents,
        subtotal:variant.price_cents*line.quantity}:null;
  }));
  const valid=$derived(priced.every(line=>line!==null));
  const total=$derived(valid?priced.reduce((sum,line)=>sum+(line?.subtotal??0),0):0);
  const money=(cents:number)=>new Intl.NumberFormat('el-CY',{style:'currency',currency:'EUR'}).format(cents/100);
  function addLine(){
    if(!selectedProduct||!selectedVariant||!Number.isSafeInteger(quantity)||quantity<1||quantity>99)return;
    const current=lines.find(l=>l.productId===selectedProduct.id&&l.variantId===selectedVariant.id);
    if(current){
      if(current.quantity+quantity>99){notice='Μέγιστη ποσότητα 99 ανά παραλλαγή.';return;}
      lines=lines.map(l=>l===current?{...l,quantity:l.quantity+quantity}:l);
    }else{
      if(lines.length>=30){notice='Μέγιστο 30 γραμμές ανά πρόχειρη παραγγελία.';return;}
      lines=[...lines,{productId:selectedProduct.id,variantId:selectedVariant.id,quantity}];
    }
    notice='';
  }
  $effect(()=>{
    void organization;
    // Changing catalog/owner clears the basket instead of mixing organizations.
    void products;void categories;
    lines=[];chosenProduct='';chosenVariant='';notice='';
  });
</script>
<section class="admin-panel" data-testid="pos-unsubmitted-basket" aria-labelledby="pos-basket-heading">
  <p class="admin-eyebrow">ΠΡΟΕΠΙΣΚΟΠΗΣΗ · ΟΧΙ ΤΑΜΕΙΟ</p>
  <h2 id="pos-basket-heading">Πρόχειρη παραγγελία</h2>
  <p class="admin-muted">Οι τιμές είναι ενδεικτικές από τον επαληθευμένο κατάλογο. Δεν καταχωρίζεται πώληση, δεν εκδίδεται απόδειξη, δεν μειώνεται απόθεμα και δεν εισπράττεται πληρωμή.</p>
  <div class="admin-pos-basket-fields">
    <label>Προϊόν
      <select value={chosenProduct} onchange={e=>{chosenProduct=e.currentTarget.value;chosenVariant='';}}>
        <option value="">Επιλογή προϊόντος</option>
        {#each sellable as product (product.id)}
          <option value={product.id}>{product.name}</option>
        {/each}
      </select>
    </label>
    <label>Παραλλαγή
      <select value={chosenVariant} disabled={!selectedProduct}
        onchange={e=>chosenVariant=e.currentTarget.value}>
        <option value="">Επιλογή παραλλαγής</option>
        {#each selectedProduct?.variants??[] as variant (variant.id)}
          <option value={variant.id}>{variant.name} · {money(variant.price_cents)}</option>
        {/each}
      </select>
    </label>
    <label>Ποσότητα
      <input type="number" min="1" max="99" step="1" bind:value={quantity}/>
    </label>
    <button type="button" class="admin-secondary" disabled={!selectedVariant} onclick={addLine}>Προσθήκη</button>
  </div>
  {#if notice}<p role="alert">{notice}</p>{/if}
  {#if !valid}<p role="alert">Ο κατάλογος άλλαξε. Καθαρίστε το πρόχειρο πριν συνεχίσετε.</p>{/if}
  {#if priced.length}
    <ul class="admin-pos-basket-lines">
      {#each priced as line,i (i)}
        {#if line}
          <li><span>{line.name} · {line.variantName} × {line.quantity}</span>
            <strong>{money(line.subtotal)}</strong>
            <button type="button" class="admin-secondary" onclick={()=>lines=lines.filter((_,j)=>j!==i)}>Αφαίρεση</button>
          </li>
        {/if}
      {/each}
    </ul>
    <p class="admin-pos-basket-total">Ενδεικτικό σύνολο: <strong>{valid?money(total):'—'}</strong></p>
    <button type="button" class="admin-secondary" onclick={()=>{lines=[];notice='';}}>Καθαρισμός</button>
  {:else}
    <p class="admin-muted">Δεν υπάρχουν προϊόντα στο πρόχειρο.</p>
  {/if}
  <p class="admin-muted">Η ολοκλήρωση πώλησης θα ενεργοποιηθεί μόνο μετά την υλοποίηση ταμείων, αποθεμάτων, συναλλαγών και αποδείξεων.</p>
</section>
<style>
.admin-pos-basket-fields{display:flex;flex-wrap:wrap;gap:12px;align-items:end;margin:14px 0}
.admin-pos-basket-fields label{display:grid;gap:6px;flex:1 1 180px}
.admin-pos-basket-fields input,.admin-pos-basket-fields select{width:100%}
.admin-pos-basket-lines{list-style:none;padding:0}
.admin-pos-basket-lines li{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px;border-top:1px solid #383838;padding:12px 0}
.admin-pos-basket-total{font-size:18px;color:#ffca46}
</style>
