<script lang="ts">
  let copied=$state(false),failed=$state(false);
  async function copy(){
    copied=false;failed=false;
    try{await navigator.clipboard.writeText(new URL('/admin/pos/terminal',window.location.origin).href);copied=true;}
    catch{failed=true;}
  }
</script>
<section class="admin-panel terminal-link" data-testid="admin-pos-install-link">
  <h2>Εφαρμογή ταμείου · Lefteria POS</h2>
  <p>Εγκαταστήστε την εφαρμογή στο κινητό, tablet ή υπολογιστή του ταμείου. Ο χειριστής συνδέεται με τον δικό του λογαριασμό ταμία.</p>
  <div class="terminal-actions">
    <a class="admin-primary" href="/admin/pos/terminal" data-sveltekit-reload target="_blank" rel="noopener">Άνοιγμα / εγκατάσταση POS</a>
    <button class="admin-secondary" type="button" onclick={()=>void copy()}>Αντιγραφή συνδέσμου POS</button>
  </div>
  <p><code>/admin/pos/terminal</code></p>
  {#if copied}<p role="status">Ο σύνδεσμος αντιγράφηκε.</p>{:else if failed}<p role="status">Ανοίξτε την εφαρμογή και αντιγράψτε τη διεύθυνση από τον browser.</p>{/if}
  <p class="admin-muted">Ο σύνδεσμος δεν παραχωρεί πρόσβαση. Απαιτείται ενεργός λογαριασμός ταμία και ανάθεση ταμείου.</p>
</section>
<style>
.terminal-link{display:grid;gap:8px;margin:12px 0}
.terminal-link p,.terminal-link h2{margin:0}
.terminal-actions{display:flex;flex-wrap:wrap;gap:10px}
</style>
