<script lang="ts">
  import {getContext,untrack} from 'svelte';
  import {browser} from '$app/env';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import {hasAdminPermission} from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type Doc={
    id:string;document_type:string;title:string;created_at:string;
    issue_date:string|null;expiry_date:string|null;expires_in_days:number|null;
    status:'no_expiry'|'expired'|'critical'|'warning'|'notice'|'watch'|'valid';
  };
  type Listing={
    organization_id:'lefteria_fc'|'lefteria_fc_academy';
    player_id:string;total:number;documents:Doc[];
  };
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  type Organization='lefteria_fc'|'lefteria_fc_academy';
  const availableOrgs=$derived($auth.status==='authenticated' && $auth.session &&
    ['manager','super_admin'].includes($auth.session.user.role)
      ? $auth.session.user.organizations.filter(o=>
        o.id==='lefteria_fc'
          ? hasAdminPermission($auth.session!.user.permissions,'teams:read')
          : o.id==='lefteria_fc_academy' &&
            hasAdminPermission($auth.session!.user.permissions,'academy:read')
        ).map(o=>o.id as Organization) : []);
  let organization=$state<Organization>('lefteria_fc_academy');
  const permitted=$derived($auth.status==='authenticated'&&!!$auth.session&&
    ['manager','super_admin'].includes($auth.session.user.role)&&
    availableOrgs.includes(organization));
  let playerId=$state('');
  let selected=$state('');
  let reload=$state(0);
  let listing=$state<Listing|null>(null);
  let loading=$state(false);
  let error=$state('');
  const validId=(x:unknown):x is string=>typeof x==='string'&&
    x.length>=1&&x.length<=128&&!/[\x00-\x1f\x7f]/.test(x);
  const types=new Set([
    'id_passport','birth_certificate','photo','registration_form',
    'kop_registration','transfer_certificate','medical_cert','vaccination_record',
    'consent_form','gdpr_consent','insurance','school_confirmation','image_rights','other'
  ]);
  const labels:Record<Doc['status'],string>={
    no_expiry:'Χωρίς λήξη',expired:'Ληγμένο',critical:'Λήγει εντός 7 ημερών',
    warning:'Λήγει εντός 30 ημερών',notice:'Λήγει εντός 60 ημερών',
    watch:'Λήγει εντός 90 ημερών',valid:'Σε ισχύ'
  };
  function validDocument(input:unknown):input is Doc{
    if(!input||typeof input!=='object'||Array.isArray(input))return false;
    const x=input as Record<string,unknown>;
    return typeof x.id==='string'&&/^[0-9a-f-]{36}$/.test(x.id)&&
      typeof x.document_type==='string'&&types.has(x.document_type)&&
      typeof x.title==='string'&&x.title.length<=200&&
      typeof x.created_at==='string'&&!Number.isNaN(Date.parse(x.created_at))&&
      (x.issue_date===null||typeof x.issue_date==='string'&&/^\d{4}-\d{2}-\d{2}$/.test(x.issue_date))&&
      (x.expiry_date===null||typeof x.expiry_date==='string'&&/^\d{4}-\d{2}-\d{2}$/.test(x.expiry_date))&&
      (x.expires_in_days===null||Number.isSafeInteger(x.expires_in_days))&&
      typeof x.status==='string'&&Object.hasOwn(labels,x.status);
  }
  function validListing(input:unknown,player:string,owner:Organization):input is Listing{
    if(!input||typeof input!=='object'||Array.isArray(input))return false;
    const x=input as Record<string,unknown>;
    return x.organization_id===owner&&
      x.player_id===player&&Number.isSafeInteger(x.total)&&
      (x.total as number)>=0&&(x.total as number)<=100&&
      Array.isArray(x.documents)&&x.documents.length===(x.total as number)&&
      x.documents.every(validDocument);
  }
  function choosePlayer(){
    if(!validId(playerId)){error='Εισάγετε έγκυρο αναγνωριστικό αθλητή.';return;}
    selected=playerId;reload++;
  }
  $effect(()=>{
    const actor=$auth.session?.user.id;
    const allowed=permitted;
    const player=selected;const owner=organization;
    void actor;void reload;
    let active=true;
    listing=null;loading=false;error='';
    if(!browser||!allowed||!validId(player))return;
    loading=true;
    untrack(()=>{void(async()=>{
      try{
        const query=new URLSearchParams({organization_id:owner});
        const response=await fetch('/api/admin/club/players/'+encodeURIComponent(player)+
          '/documents?'+query.toString(),{
          credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}
        });
        if(!active)return;
        if(response.status===401||response.status===403){
          void auth.bootstrap();throw new Error('Η σύνδεση ή η πρόσβασή σας άλλαξε.');
        }
        if(!response.ok)throw new Error('Τα έγγραφα δεν είναι διαθέσιμα.');
        const result:unknown=await response.json();
        if(!validListing(result,player,owner))throw new Error('Μη επαληθευμένα έγγραφα.');
        if(active)listing=result;
      }catch(e){
        if(active){listing=null;error=e instanceof Error?e.message:'Σφάλμα ανάγνωσης.';}
      }finally{if(active)loading=false;}
    })();});
    return()=>{active=false;};
  });
</script>
<svelte:head><title>Ιδιωτικά έγγραφα Ακαδημίας | Λευτεριά FC OS</title></svelte:head>
{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="documents" busy={$auth.busy} onLogout={()=>void auth.logout()}>
    <section class="mx-auto max-w-4xl space-y-5 px-4 py-6">
      <p class="text-xs uppercase tracking-widest text-amber-500">Ακαδημία · Προστατευμένα αρχεία</p>
      <h1 class="text-2xl font-bold">Έγγραφα αθλητών</h1>
      <p class="text-sm text-zinc-400">
        Προβάλλονται μόνο κατηγορίες, ημερομηνίες και κατάσταση λήξης. Τα ίδια τα αρχεία,
        οι μεταφορτώσεις και οι λήψεις δεν έχουν ακόμη ενεργοποιηθεί.
      </p>
      <AdminSessionStatus />
      {#if permitted}
        <label class="flex flex-col gap-1 text-sm">Οργανισμός
          <select class="rounded-lg border border-zinc-700 bg-zinc-900 p-3 text-white"
            bind:value={organization} onchange={()=>{selected='';listing=null;}}>
            {#each availableOrgs as org}
              <option value={org}>{org==='lefteria_fc'?'Πρώτη ομάδα':'Ακαδημία'}</option>
            {/each}
          </select>
        </label>
        <form class="flex flex-wrap gap-3" onsubmit={e=>{e.preventDefault();choosePlayer();}}>
          <label class="flex flex-1 flex-col gap-1 text-sm">
            Αναγνωριστικό αθλητή
            <input class="rounded-lg border border-zinc-700 bg-zinc-900 p-3 text-white"
              bind:value={playerId} required maxlength="128" />
          </label>
          <button type="submit" class="self-end rounded-lg bg-amber-500 px-5 py-3 font-semibold text-black">Προβολή</button>
        </form>
        {#if loading}<p role="status">Φόρτωση μεταδεδομένων…</p>{/if}
        {#if error}<p role="alert" class="text-red-400">{error}</p>{/if}
        {#if listing}
          <p class="text-sm text-zinc-400">Έγγραφα: {listing.total}</p>
          {#each listing.documents as document (document.id)}
            <article class="rounded-xl border border-zinc-700 bg-zinc-900 p-5">
              <div class="flex flex-wrap items-start justify-between gap-2">
                <div>
                  <h2 class="font-semibold">{document.title}</h2>
                  <p class="text-xs text-zinc-400">{document.document_type}</p>
                </div>
                <span class="text-sm">{labels[document.status]}</span>
              </div>
              <p class="mt-2 text-sm text-zinc-400">
                Ημερομηνία λήξης: {document.expiry_date ?? 'Δεν ορίζεται'}
              </p>
            </article>
          {/each}
          {#if listing.total===0}<p>Δεν έχουν καταχωριστεί έγγραφα για αυτόν τον αθλητή.</p>{/if}
        {/if}
      {:else}
        <p>Η προβολή εγγράφων απαιτεί εξουσιοδοτημένο διαχειριστή της Ακαδημίας.</p>
      {/if}
    </section>
  </AdminFrame>
{:else}
  <AdminSessionStatus />
{/if}
