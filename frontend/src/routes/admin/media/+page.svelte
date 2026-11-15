<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import PublishedMediaBrowser from '../../../lib/admin/PublishedMediaBrowser.svelte';
  import type { PublicOrganization } from '../../../lib/public/types.ts';
  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const permitted=$derived($auth.status==='authenticated'&&$auth.session&&
    hasAdminPermission($auth.session.user.permissions,'gallery:read')?
    $auth.session.user.organizations.filter(o=>o.id==='lefteria_fc'||o.id==='lefteria_fc_academy'):[]);
  let organization=$state<PublicOrganization|null>(null);
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  $effect(()=>{if(!permitted.some(o=>o.id===organization))
    organization=(permitted[0]?.id as PublicOrganization|undefined)??null;});
  function chooseOwner(next:PublicOrganization){if(permitted.some(o=>o.id===next))organization=next;}
</script>
<svelte:head><title>Βιβλιοθήκη εικόνων | Λευτεριά FC</title></svelte:head>
{#if $auth.status==='authenticated'&&$auth.session}
  <AdminFrame user={$auth.session.user} active="media" busy={$auth.busy} onLogout={()=>void auth.logout()}>
    <div class="media-admin" data-testid="admin-media-library">
      <header class="media-head">
        <p class="admin-eyebrow">ΙΣΤΟΣΕΛΙΔΑ · ΠΟΛΥΜΕΣΑ</p>
        <h1>Βιβλιοθήκη εικόνων</h1>
        <p class="admin-muted">Αναζητήστε ήδη δημοσιευμένες φωτογραφίες, εικόνες χορηγών, συνεργατών και εκδηλώσεων ανά οργανισμό. Αντιγράψτε διευθύνσεις για τον Visual Builder.</p>
        <p class="media-safety">Πρώτη έκδοση μόνο ανάγνωσης. Η μεταφόρτωση, διαγραφή και διαχείριση αδειών χρήσης δεν έχουν ακόμη ενεργοποιηθεί.</p>
      </header>
      {#if permitted.length&&organization}
        <section class="admin-panel media-owner">
          <label>Οργανισμός <select value={organization} aria-label="Οργανισμός βιβλιοθήκης"
            onchange={e=>chooseOwner(e.currentTarget.value as PublicOrganization)}>
              {#each permitted as o (o.id)}<option value={o.id}>{o.id==='lefteria_fc'?'Πρώτη ομάδα':'Ακαδημία'}</option>{/each}
            </select>
          </label>
          <span class="admin-muted">Χωριστή δημοσιευμένη συλλογή για κάθε οργανισμό.</span>
        </section>
        <section class="admin-panel media-library"><PublishedMediaBrowser {organization}/></section>
      {:else}
        <section class="admin-panel"><p role="alert">Δεν έχετε δικαίωμα ανάγνωσης ή εγκεκριμένο οργανισμό.</p></section>
      {/if}
    </div>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
.media-admin{color:#f3f3ef;display:grid;gap:18px}
.media-head h1{font:italic 900 clamp(34px,4vw,55px)/1.05 'Fira Sans Extra Condensed',sans-serif;margin:7px 0 12px}
.media-head .admin-muted{max-width:75ch}
.media-safety{border-left:3px solid #e1b34b;background:#251f12;color:#f3dbab;padding:12px 15px;max-width:80ch;font-size:13px}
.media-owner{display:flex;align-items:end;gap:20px;flex-wrap:wrap;padding:17px}
.media-owner label{display:grid;gap:7px;min-width:min(300px,100%);font-size:12px;font-weight:800}
.media-owner select{background:#121618;color:#fff;border:1px solid #6c6758;border-radius:5px;padding:11px}
.media-library{padding:clamp(14px,2vw,24px);min-width:0}
.media-admin :is(select,a,button):focus-visible{outline:3px solid #f8cb68;outline-offset:2px}
</style>
