<script lang="ts">
  import {getContext,onMount} from 'svelte';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../../lib/admin/controller.ts';
  import AdminPosCashierShiftsPanel from '../../../../lib/admin/AdminPosCashierShiftsPanel.svelte';
  import {appInstall,type InstallState} from '../../../../lib/mobile/app-install.ts';
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  let username=$state(''),password=$state(''),showHelp=$state(false);
  let installState=$state<InstallState>({installed:false,available:false,busy:false,ios:false,android:false});
  onMount(()=>appInstall.subscribe(state=>installState=state));
  const owner=$derived($auth.status==='authenticated'&&$auth.session?.user.role==='cashier'
    ? $auth.session.user.organizations.find(org=>org.id==='lefteria_fc'||org.id==='lefteria_fc_academy')?.id as 'lefteria_fc'|'lefteria_fc_academy'|undefined
    :undefined);
  async function login(e:SubmitEvent){
    e.preventDefault();if($auth.busy)return;
    try{await auth.login(username,password,null);}finally{password='';}
  }
  async function install(){
    const result=await appInstall.install();
    if(result==='help')showHelp=true;
  }
</script>
<svelte:head><title>Lefteria POS · Ταμείο</title></svelte:head>
<main class="terminal-shell" data-testid="pos-standalone-terminal">
  <header class="terminal-header">
    <img src="/os/icon-192.png" width="48" height="48" alt=""/>
    <div><p class="admin-eyebrow">LEFTERIA FC</p><h1>POS · Ταμείο</h1></div>
    {#if $auth.status==='authenticated'}<button type="button" class="admin-secondary" disabled={$auth.busy} onclick={()=>void auth.logout()}>Αποσύνδεση</button>{/if}
  </header>
  {#if !installState.installed}
    <section class="admin-panel terminal-install" data-testid="pos-install-controls">
      <button type="button" class="admin-primary" disabled={installState.busy} onclick={()=>void install()}>Εγκατάσταση POS</button>
      <button type="button" class="admin-secondary" aria-expanded={showHelp} onclick={()=>showHelp=!showHelp}>Οδηγίες εγκατάστασης</button>
      {#if showHelp}<div role="status">
        <p>Android / υπολογιστής: ανοίξτε το POS σε Chrome ή Edge και επιλέξτε Εγκατάσταση εφαρμογής από το μενού, εφόσον υποστηρίζεται.</p>
        <p>iPhone / iPad: Safari → Κοινοποίηση → Προσθήκη στην οθόνη Αφετηρίας.</p>
        <p>Η εγκατάσταση δεν αποθηκεύει πωλήσεις εκτός σύνδεσης. Απαιτείται σύνδεση στο διαδίκτυο.</p>
      </div>{/if}
    </section>
  {/if}
  {#if $auth.status==='anonymous'}
    <section class="admin-panel terminal-login">
      <h2>Σύνδεση ταμία</h2>
      <p>Χρησιμοποιήστε τον λογαριασμό που δημιούργησε ο Super Admin.</p>
      {#if $auth.error}<p role="alert">{$auth.error}</p>{/if}
      <form onsubmit={login}>
        <label>Όνομα χρήστη <input bind:value={username} required maxlength="80" autocomplete="username" autocapitalize="none" disabled={$auth.busy}/></label>
        <label>Κωδικός <input type="password" bind:value={password} required autocomplete="current-password" disabled={$auth.busy}/></label>
        <button type="submit" class="admin-primary" disabled={$auth.busy}>{$auth.busy?'Σύνδεση…':'Σύνδεση'}</button>
      </form>
    </section>
  {:else if $auth.status==='authenticated'&&$auth.session?.user.role==='cashier'&&owner}
    {#key $auth.session.user.id+':'+owner}
      <AdminPosCashierShiftsPanel {owner} actorId={$auth.session.user.id}
        onExpired={()=>void auth.bootstrap()} onRevoked={()=>void auth.bootstrap()}/>
    {/key}
  {:else if $auth.status==='authenticated'}
    <section class="admin-panel"><p role="alert">Η εφαρμογή ταμείου απαιτεί ενεργό λογαριασμό ταμία με ανάθεση οργανισμού. Αποσυνδεθείτε πριν χρησιμοποιήσετε διαφορετικό λογαριασμό.</p></section>
  {:else if $auth.status==='unavailable'}
    <section class="admin-panel"><p role="alert">Η σύνδεση δεν είναι διαθέσιμη. Ελέγξτε το δίκτυο.</p><button class="admin-secondary" type="button" disabled={$auth.busy} onclick={()=>void auth.bootstrap()}>Δοκιμή ξανά</button></section>
  {:else}<p role="status">Έλεγχος σύνδεσης…</p>{/if}
</main>
<style>
.terminal-shell{width:min(100%,1200px);margin-inline:auto;padding:16px;box-sizing:border-box;min-height:100dvh}
.terminal-header{display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:16px}
.terminal-header h1,.terminal-header p{margin:0}
.terminal-header button{margin-left:auto}
.terminal-install{display:flex;gap:8px;align-items:start;flex-wrap:wrap;margin-bottom:16px}
.terminal-install div{flex-basis:100%}
.terminal-login{max-width:460px;margin:24px auto}
.terminal-login form,.terminal-login label{display:grid;gap:8px}
.terminal-login form{gap:16px}
</style>
