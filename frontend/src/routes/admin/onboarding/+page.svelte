<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import GuidedSetup from '../../../lib/onboarding/GuidedSetup.svelte';
  import {adminGuide} from '../../../lib/onboarding/guides.ts';

  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  $effect(()=>{if(browser && $auth.status==='anonymous')void goto('/admin/login',{replace:true});});
</script>

<svelte:head><title>Οδηγός διαχείρισης | Λευτεριά FC</title></svelte:head>

{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="onboarding" busy={$auth.busy}
    onLogout={()=>void auth.logout()}>
    <p class="admin-eyebrow">ΟΡΓΑΝΩΣΗ · ΒΗΜΑ ΠΡΟΣ ΒΗΜΑ</p>
    <h1>Ξεκινώντας με το Lefteria FC OS</h1>
    <p class="admin-muted">Ο οδηγός προσαρμόζεται στον ενεργό ρόλο και τα δικαιώματα του λογαριασμού σας. Η ολοκλήρωση ελέγχεται με πραγματικά δεδομένα όπου υποστηρίζεται και δεν ξεκλειδώνει πρόσθετα δικαιώματα.</p>
    {#key $auth.session.user.id+':'+$auth.session.user.role}
      <GuidedSetup
        steps={adminGuide($auth.session.user)}
        scope={'admin:'+$auth.session.user.id+':'+$auth.session.user.role}
        title="Ο προσωπικός σου οδηγός"
        onNavigate={async href=>{await goto(href);}}/>
    {/key}
    <section class="admin-panel">
      <h2>Ολοκλήρωση προφίλ διαχειριστή</h2>
      <p class="admin-muted">Το όνομα και ο ρόλος ελέγχονται στην επισκόπηση λογαριασμού. Η αλλαγή του προφίλ διαχειριστή δεν παρέχεται ακόμη από το ασφαλές backend, γι' αυτό δεν εμφανίζουμε μη λειτουργική αποθήκευση.</p>
      <a href="/admin#account">Έλεγχος λογαριασμού</a>
    </section>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
