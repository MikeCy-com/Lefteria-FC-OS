<script lang="ts">
  import { getContext, onMount } from 'svelte';
  import { goto } from '$app/navigation';
  import { page } from '$app/state';
  import { mobileAfterLogin, readMobileTab } from '../../../lib/mobile/guest-navigation.ts';
  import { TAB_NAMES } from '../../../lib/mobile/navigation.ts';
  import { browser } from '$app/env';
  import MobileLogin from '../../../lib/mobile/components/MobileLogin.svelte';
  import AppInstallButton from '../../../lib/public/AppInstallButton.svelte';
  import { MOBILE_AUTH_CONTEXT, type MobileAuthController } from '../../../lib/mobile/controller.ts';
  import type { RememberDays } from '../../../lib/mobile/components/component-types.ts';

  const controller = getContext<MobileAuthController>(MOBILE_AUTH_CONTEXT);
  let phone = $state('');
  let rememberDays = $state<RememberDays>(null);
  let clock = $state(Date.now());
  const confirmedPhone = $derived($controller.phone);
  const resendSeconds = $derived(Math.max(0, Math.ceil(($controller.resendAvailableAt - clock) / 1000)));
  const returnTab = $derived(readMobileTab(page.url.searchParams.get('return_tab')));
  $effect(() => {
    if (browser && $controller.status === 'authenticated' && $controller.user)
      void goto(mobileAfterLogin(returnTab, $controller.user.role, $controller.user.current_role_available), { replace: true });
  });
  $effect(() => { if (confirmedPhone) phone = confirmedPhone; });
  onMount(() => {
    const interval = setInterval(() => { clock = Date.now(); }, 1000);
    return () => clearInterval(interval);
  });
</script>

<svelte:head><title>Σύνδεση | Λευτεριά FC</title><meta name="robots" content="noindex" /></svelte:head>

{#if $controller.status === 'anonymous'}
  <aside class="os-install-entry" data-testid="mobile-login-os-install">
    <div>
      <strong>Lefteria FC OS</strong>
      {#if returnTab && returnTab !== 'home'}
        <span data-testid="login-intended-tab">Συνδέσου με τον διαθέσιμο ρόλο για πρόσβαση: <strong>{TAB_NAMES[returnTab]}</strong></span>
      {:else}
        <span>Η επίσημη εφαρμογή για το κινητό σου</span>
      {/if}
    </div>
    <a class="guest-login-link" href="/app" data-testid="continue-as-guest">← Συνέχεια ως επισκέπτης</a>
    <AppInstallButton />
  </aside>
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
  .os-install-entry{display:flex;justify-content:space-between;align-items:center;gap:12px;
    padding:12px clamp(14px,3vw,24px);background:#111313;color:#fff;
    border-bottom:1px solid #b4822e;flex-wrap:wrap}
  .os-install-entry>div{display:grid;gap:2px}
  .os-install-entry strong{font-size:15px}
  .os-install-entry span{font-size:12px;color:#b7bec2}
  .os-install-entry :global(.public-app-open){min-height:44px;padding:10px 14px}
  .guest-login-link{display:inline-flex;align-items:center;min-height:44px;color:#f6bb38;font:700 13px/1.5 'Fira Sans',sans-serif;text-decoration:none}
  .guest-login-link:focus-visible{outline:2px solid #f6bb38;outline-offset:3px}
  .os-install-entry span strong{font-size:inherit}
  @media(max-width:600px){.os-install-entry{gap:4px 12px}.os-install-entry>div{flex:1 0 100%}}
  .login-status { display:grid; align-content:center; justify-items:center; gap:12px; min-height:100dvh; padding:24px; background:#080808; color:#fff; font-family:'Fira Sans',sans-serif; }
  .login-status p { max-width:32rem; text-align:center; }
  button { min-height:44px; padding:10px 18px; border:1px solid #f6bb38; border-radius:6px; background:#161616; color:#ffca46; cursor:pointer; }
  button:focus-visible { outline:2px solid #ffca46; outline-offset:3px; }
</style>
