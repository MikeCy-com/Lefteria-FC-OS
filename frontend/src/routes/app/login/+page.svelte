<script lang="ts">
  import { getContext, onMount } from 'svelte';
  import { goto } from '$app/navigation';
  import { browser } from '$app/env';
  import MobileLogin from '../../../lib/mobile/components/MobileLogin.svelte';
  import { MOBILE_AUTH_CONTEXT, type MobileAuthController } from '../../../lib/mobile/controller.ts';
  import type { RememberDays } from '../../../lib/mobile/components/component-types.ts';

  const controller = getContext<MobileAuthController>(MOBILE_AUTH_CONTEXT);
  let phone = $state('');
  let rememberDays = $state<RememberDays>(null);
  let clock = $state(Date.now());
  const confirmedPhone = $derived($controller.phone);
  const resendSeconds = $derived(Math.max(0, Math.ceil(($controller.resendAvailableAt - clock) / 1000)));
  $effect(() => { if (browser && $controller.status === 'authenticated') void goto('/app', { replace: true }); });
  $effect(() => { if (confirmedPhone) phone = confirmedPhone; });
  onMount(() => {
    const interval = setInterval(() => { clock = Date.now(); }, 1000);
    return () => clearInterval(interval);
  });
</script>

<svelte:head><title>Σύνδεση | Λευτεριά FC</title><meta name="robots" content="noindex" /></svelte:head>

{#if $controller.status === 'anonymous'}
  <MobileLogin step={$controller.step} bind:phone bind:rememberDays identities={$controller.identities}
    detectedRole={$controller.detectedRole} busy={$controller.busy} error={$controller.error} {resendSeconds}
    onRequestOtp={async (value) => { await controller.requestOtp(value); }}
    onVerifyOtp={async (code, selection) => { await controller.verifyOtp(code, selection); }}
    onChooseRole={async (role, selection) => { await controller.chooseRole(role, selection); }}
    onRegisterFan={async (registration) => { await controller.registerFan(registration); }}
    onStepChange={(step) => controller.changeStep(step)} />
{:else}
  <div class="login-status">
    {#if $controller.status === 'unavailable'}
      <p role="alert">{$controller.error}</p>
      <button type="button" disabled={$controller.busy} onclick={() => void controller.bootstrap()}>Δοκιμή ξανά</button>
      <button type="button" disabled={$controller.busy} onclick={() => controller.logout()}>Νέα σύνδεση</button>
    {:else}<p role="status">Έλεγχος σύνδεσης…</p>{/if}
  </div>
{/if}

<style>
  .login-status { display:grid; align-content:center; justify-items:center; gap:12px; min-height:100dvh; padding:24px; background:#080808; color:#fff; font-family:'Fira Sans',sans-serif; }
  .login-status p { max-width:32rem; text-align:center; }
  button { min-height:44px; padding:10px 18px; border:1px solid #f5a623; border-radius:6px; background:#161616; color:#ffca46; cursor:pointer; }
  button:focus-visible { outline:2px solid #ffca46; outline-offset:3px; }
</style>
