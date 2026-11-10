<script lang="ts">
  import { getContext, untrack } from 'svelte';
  import { browser } from '$app/env';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';

  type Row = {
    id:string; created_at:string; edit_until:string; revision:number;
    summary:string|null; notes:string|null;
    technical:Record<string,number>; tactical:Record<string,number>;
    physical:Record<string,number>; character:Record<string,number>;
    overall:Record<string,unknown>;
  };
  type Timeline = {
    organization_id:'lefteria_fc'|'lefteria_fc_academy';
    player_id:string; total:number; evaluations:Row[];
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
  $effect(()=>{if(availableOrgs.length && !availableOrgs.includes(organization))organization=availableOrgs[0];});
  const allowed=$derived($auth.status==='authenticated' && !!$auth.session &&
    ['manager','super_admin'].includes($auth.session.user.role) &&
    availableOrgs.includes(organization));
  let playerId=$state('');
  let selected=$state('');
  let timeline=$state<Timeline|null>(null);
  let loading=$state(false);
  let error=$state('');
  let requestKey=$state(0);
  const validId=(x:unknown):x is string =>
    typeof x==='string' && x.length>=1 && x.length<=128 &&
    !/[\x00-\x1f\x7f]/.test(x);
  function validRow(value:unknown):value is Row {
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const row=value as Record<string,unknown>;
    return typeof row.id==='string' && /^[0-9a-f-]{36}$/.test(row.id) &&
      typeof row.created_at==='string' && !Number.isNaN(Date.parse(row.created_at)) &&
      typeof row.edit_until==='string' && !Number.isNaN(Date.parse(row.edit_until)) &&
      Number.isInteger(row.revision) && (row.revision as number)>=1 &&
      (row.revision as number)<=10000 &&
      (row.summary===null||typeof row.summary==='string'&&row.summary.length<=500) &&
      (row.notes===null||typeof row.notes==='string'&&row.notes.length<=4000) &&
      ['technical','tactical','physical','character','overall'].every(k=>
        row[k]!==null && typeof row[k]==='object' && !Array.isArray(row[k]));
  }
  function validTimeline(value:unknown,expected:string,owner:Organization):value is Timeline {
    if(!value||typeof value!=='object'||Array.isArray(value))return false;
    const x=value as Record<string,unknown>;
    return x.organization_id===owner &&
      x.player_id===expected && Number.isInteger(x.total) &&
      (x.total as number)>=0 && (x.total as number)<=200 &&
      Array.isArray(x.evaluations) && x.evaluations.length<201 &&
      x.evaluations.every(validRow);
  }
  function selectPlayer(){
    if(!validId(playerId)){error='Εισάγετε έγκυρο αναγνωριστικό αθλητή.';return;}
    selected=playerId;
    requestKey++;
  }
  $effect(()=>{
    const user=$auth.session?.user.id;
    const activePermission=allowed;
    const player=selected;const owner=organization;
    void requestKey;void user;
    let active=true;
    timeline=null;
    if(!browser||!activePermission||!validId(player))return;
    loading=true;error='';
    untrack(()=>{void(async()=>{
      try{
        const q=new URLSearchParams({organization_id:owner});
        const response=await fetch('/api/admin/club/players/'+encodeURIComponent(player)+
          '/evaluations?'+q.toString(),{
          credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}
        });
        if(!active)return;
        if(response.status===401||response.status===403){
          void auth.bootstrap();throw new Error('Η πρόσβαση δεν επιτρέπεται.');
        }
        if(!response.ok)throw new Error('Οι αξιολογήσεις δεν είναι διαθέσιμες.');
        const result:unknown=await response.json();
        if(!validTimeline(result,player,owner))throw new Error('Μη επαληθευμένη απόκριση.');
        if(active)timeline=result;
      }catch(e){
        if(active){timeline=null;error=e instanceof Error?e.message:'Σφάλμα ανάγνωσης.';}
      }finally{if(active)loading=false;}
    })();});
    return()=>{active=false;};
  });
</script>

<svelte:head><title>Αξιολογήσεις αθλητών | Λευτεριά FC OS</title></svelte:head>
{#if $auth.status==='authenticated' && $auth.session}
<AdminFrame user={$auth.session.user} active="evaluations" busy={$auth.busy} onLogout={() => void auth.logout()}>
  <section class="mx-auto max-w-4xl space-y-5 px-4 py-6">
    <div>
      <p class="text-xs uppercase tracking-widest text-amber-500">Ομάδες · Ιδιωτική διαχείριση</p>
      <h1 class="text-2xl font-bold">Αξιολογήσεις αθλητών</h1>
      <p class="text-sm text-zinc-400">Μόνο ανάγνωση. Δεν διατίθενται ακόμη δημιουργία ή επεξεργασία αξιολογήσεων.</p>
    </div>
    <AdminSessionStatus />
    {#if allowed}
      <label class="flex flex-col gap-1 text-sm">Οργανισμός
        <select class="rounded-lg border border-zinc-700 bg-zinc-900 p-3 text-white"
          bind:value={organization} onchange={()=>{selected='';timeline=null;}}>
          {#each availableOrgs as org}
            <option value={org}>{org==='lefteria_fc'?'Πρώτη ομάδα':'Ακαδημία'}</option>
          {/each}
        </select>
      </label>
      <form class="flex flex-wrap gap-3" onsubmit={(event)=>{event.preventDefault();selectPlayer();}}>
        <label class="flex flex-1 flex-col gap-1 text-sm">
          Αναγνωριστικό αθλητή
          <input class="rounded-lg border border-zinc-700 bg-zinc-900 p-3 text-white"
            maxlength="128" required bind:value={playerId}
            placeholder="Αναγνωριστικό από τον κατάλογο αθλητών" />
        </label>
        <button class="self-end rounded-lg bg-amber-500 px-5 py-3 font-semibold text-black" type="submit">
          Προβολή
        </button>
      </form>
      {#if loading}<p role="status">Φόρτωση αξιολογήσεων…</p>{/if}
      {#if error}<p role="alert" class="text-red-400">{error}</p>{/if}
      {#if timeline}
        <p class="text-sm text-zinc-400">Καταχωρήσεις: {timeline.total}</p>
        {#each timeline.evaluations as evaluation (evaluation.id)}
          <article class="space-y-3 rounded-xl border border-zinc-700 bg-zinc-900 p-5">
            <div class="flex flex-wrap justify-between gap-2">
              <h2 class="font-semibold">{evaluation.summary||'Αξιολόγηση χωρίς τίτλο'}</h2>
              <span class="text-xs text-zinc-400">
                {new Date(evaluation.created_at).toLocaleDateString('el-CY')} · Έκδοση {evaluation.revision}
              </span>
            </div>
            {#if evaluation.notes}<p class="whitespace-pre-wrap text-sm">{evaluation.notes}</p>{/if}
            {#each ['technical','tactical','physical','character'] as section}
              <details class="rounded-lg border border-zinc-700 p-3">
                <summary class="cursor-pointer text-sm font-medium">{section}</summary>
                <dl class="mt-2 grid grid-cols-2 gap-2 text-sm">
                  {#each Object.entries(evaluation[section as 'technical'|'tactical'|'physical'|'character']) as [key,value]}
                    <div><dt class="text-zinc-400">{key}</dt><dd>{value}/100</dd></div>
                  {/each}
                </dl>
              </details>
            {/each}
          </article>
        {/each}
        {#if timeline.total===0}<p>Δεν υπάρχουν ακόμη αξιολογήσεις για αυτόν τον αθλητή.</p>{/if}
      {/if}
    {:else}
      <p class="rounded-xl border border-zinc-700 p-5">Απαιτείται εξουσιοδοτημένη συνεδρία διαχείρισης ομάδας.</p>
    {/if}
  </section>
</AdminFrame>
{:else}
  <AdminSessionStatus />
{/if}
