<script lang="ts">
  import {onMount} from 'svelte';
  import type {MobileAuthController} from '../controller.ts';
  import {hasDevicePushSupport,browserPushSubscription,
    enableBrowserPush,disableBrowserPush,
    type DevicePushConfig} from '../web-push.ts';
  let {controller}:{controller:MobileAuthController}=$props();
  let supported=$state(false);
  let settings=$state<DevicePushConfig|null>(null);
  let subscribed=$state(false);
  let busy=$state(true);
  let error=$state<string|null>(null);
  let success=$state<string|null>(null);
  let alive=false;
  onMount(()=>{
    alive=true;supported=hasDevicePushSupport();void refresh();
    return()=>{alive=false;};
  });
  async function refresh(){
    busy=true;error=null;
    try{
      const config=await controller.getDevicePushConfig();
      const device=await browserPushSubscription();
      if(!alive)return;
      settings=config;
      subscribed=!!device;
    }catch{
      if(alive)error='Δεν φορτώθηκαν οι ρυθμίσεις ειδοποιήσεων συσκευής.';
    }finally{if(alive)busy=false;}
  }
  async function enable(){
    if(busy||!supported||!settings?.enabled||!settings.public_key)return;
    busy=true;error=null;success=null;
    try{
      const result=await enableBrowserPush(settings.public_key,
        (endpoint,p256dh,auth)=>controller.subscribeDevicePush(endpoint,p256dh,auth));
      if(!alive)return;
      if(result){
        subscribed=true;
        success='Η συσκευή ενεργοποιήθηκε για νέες ανακοινώσεις.';
      }
    }catch(e){
      if(alive)error=e instanceof Error?e.message:'Η ενεργοποίηση απέτυχε.';
    }finally{if(alive)busy=false;}
  }
  async function disable(){
    if(busy||!subscribed)return;
    busy=true;error=null;success=null;
    try{
      const result=await disableBrowserPush(endpoint=>
        controller.unsubscribeDevicePush(endpoint));
      if(!alive)return;
      if(!result)throw new Error('Το πρόγραμμα περιήγησης δεν επιβεβαίωσε την απενεργοποίηση.');
      subscribed=false;success='Απενεργοποιήθηκαν οι ειδοποιήσεις αυτής της συσκευής.';
    }catch(e){if(alive)error=e instanceof Error?e.message:'Η απενεργοποίηση απέτυχε.';}
    finally{if(alive)busy=false;}
  }
</script>

<section class="push-controls" aria-labelledby="device-push-heading"
  data-testid="web-push-controls">
  <h2 id="device-push-heading">Ειδοποιήσεις συσκευής</h2>
  <p>Επίλεξε αν θέλεις ειδοποίηση και όταν η εφαρμογή δεν είναι ανοιχτή.
    Η ειδοποίηση δείχνει μόνο ένα γενικό μήνυμα και ανοίγει τη λίστα
    ανακοινώσεων. Δεν περιέχει ονόματα ή κείμενο ανακοίνωσης.</p>
  {#if !supported&&!busy}
    <p class="note" role="status">Αυτό το πρόγραμμα περιήγησης δεν υποστηρίζει
      ειδοποιήσεις συσκευής. Σε iPhone/iPad μπορεί να χρειάζεται προσθήκη
      της εφαρμογής στην αρχική οθόνη.</p>
  {:else if settings&&!settings.enabled}
    <p class="note" role="status">Οι ειδοποιήσεις συσκευής δεν είναι ακόμα
      ενεργοποιημένες για τον σύλλογο. Οι ειδοποιήσεις μέσα στην εφαρμογή
      λειτουργούν κανονικά.</p>
  {:else if settings?.enabled&&supported}
    {#if subscribed}
      <p class="active" role="status">Ενεργές σε αυτή τη συσκευή</p>
      <button type="button" class="outline" disabled={busy}
        onclick={()=>void disable()}>Απενεργοποίηση συσκευής</button>
    {:else}
      <button type="button" class="primary" disabled={busy}
        onclick={()=>void enable()}>Ενεργοποίηση ειδοποιήσεων</button>
    {/if}
    <p class="count">Ενεργές συσκευές στον λογαριασμό: {settings.active_devices}</p>
    <p class="note">Η ενεργοποίηση απαιτεί άδεια από το πρόγραμμα περιήγησης
      και ενεργή προτίμηση «Ενημερώσεις συλλόγου» στο προφίλ.</p>
  {/if}
  {#if busy}<p role="status">Έλεγχος συσκευής…</p>{/if}
  {#if error}<p class="error" role="alert">{error}</p>{/if}
  {#if success}<p class="active" role="status">{success}</p>{/if}
</section>
<style>
  .push-controls{margin:18px 0;padding:16px;border-radius:12px;
    background:var(--lf-elevated);border:1px solid var(--lf-line);
    font-family:var(--lf-body);font-size:13px}
  h2{margin:0 0 8px;font:700 21px/1.2 var(--lf-heading);color:var(--lf-gold-light)}
  p{margin:9px 0;color:var(--lf-muted);line-height:1.55}
  .primary,.outline{min-height:44px;padding:10px 16px;border-radius:9px;
    font:700 13px var(--lf-body);cursor:pointer}
  .primary{background:var(--lf-gold);border:1px solid var(--lf-gold);color:#080808}
  .outline{background:transparent;color:var(--lf-gold-light);
    border:1px solid var(--lf-brand-edge)}
  .active{color:#bdecc5}
  .error{color:#ffd2c7}
  .note,.count{font-size:11px}
  button:disabled{opacity:.55;cursor:default}
  button:focus-visible{outline:2px solid var(--lf-gold);outline-offset:3px}
</style>
