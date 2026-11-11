<script lang="ts">
  import { tick } from 'svelte';
  import type { Snippet } from 'svelte';
  import { ROLE_NAMES, TAB_NAMES, canComposePost, primaryTabs } from '../navigation.ts';
  import type { MobileRole, MobileTab } from '../navigation.ts';
  import type { MobileIconName, UiAction } from './component-types.ts';
  import MobileIcon from './MobileIcon.svelte';
  import './mobile-theme.css';

  interface Props {
    role: MobileRole;
    activeTab: MobileTab;
    availableRoles: readonly MobileRole[];
    userName: string;
    avatarUrl?: string | null;
    notificationCount?: number;
    busy?: boolean;
    children: Snippet;
    onTabChange: (tab: MobileTab) => UiAction;
    onRoleChange: (role: MobileRole) => UiAction;
    onNotifications: () => UiAction;
    onProfile: () => UiAction;
    onCreatePost: () => UiAction;
  }

  let { role, activeTab, availableRoles, userName, avatarUrl = null, notificationCount = 0,
    busy = false, children, onTabChange, onRoleChange, onNotifications, onProfile, onCreatePost }: Props = $props();
  const id = $props.id();
  let roleMenuOpen = $state(false);
  let avatarFailed = $state(false);
  let roleTrigger = $state<HTMLButtonElement>();
  let roleMenu = $state<HTMLDivElement>();
  function dismissRoles(event: PointerEvent) {
    if (!roleMenuOpen) return;
    const target = event.target;
    if (!(target instanceof Node)) return;
    if (roleMenu?.contains(target) || roleTrigger?.contains(target)) return;
    roleMenuOpen = false;
  }
  const roles = $derived([...new Set(availableRoles)]);
  const tabs = $derived(primaryTabs(role));
  const hasComposer = $derived(canComposePost(role));
  const initial = $derived(userName.trim().slice(0, 1).toLocaleUpperCase('el-GR') || '?');

  $effect(() => { if (avatarUrl) avatarFailed = false; });
  $effect(() => { if (busy) roleMenuOpen = false; });
  $effect(() => {
    if (roleMenuOpen) void tick().then(() => roleMenu?.querySelector('button')?.focus());
  });

  function closeRoles(event: KeyboardEvent) {
    if (event.key === 'Escape') {
      roleMenuOpen = false;
      roleTrigger?.focus();
    }
  }

  function iconForTab(tab: MobileTab): MobileIconName {
    if (tab === 'home') return 'home';
    if (tab === 'calendar' || tab === 'fixtures') return 'calendar';
    if (tab === 'chat') return 'messages';
    if (tab === 'more') return 'menu';
    if (tab === 'financials') return 'wallet';
    if (tab === 'shop') return 'cart';
    if (tab === 'matches' || tab === 'gamehub' || tab === 'potm') return 'trophy';
    if (tab === 'profile') return 'user';
    return 'users';
  }
</script>

<svelte:window onpointerdown={dismissRoles} />
<div class="lf-mobile-theme mobile-shell" data-mobile-role={role} data-testid="mobile-app" aria-busy={busy}>
  <a class="skip-link" href={`#${id}-content`}>Μετάβαση στο περιεχόμενο</a>
  <header class="mobile-header">
    <button bind:this={roleTrigger} type="button" class="header-action role-trigger" aria-label="Αλλαγή ρόλου"
      aria-expanded={roleMenuOpen} aria-controls={`${id}-roles`} disabled={busy}
      onclick={() => { roleMenuOpen = !roleMenuOpen; }} onkeydown={closeRoles} data-testid="role-switcher">
      <MobileIcon name="menu" size={24} />
    </button>
    <img class="club-crest" src="/mobile/club-logo.png" alt="" width="42" height="48" />
    <div class="brand-lockup"><p>ΛΕΥΤΕΡΙΑ FC</p><span>{ROLE_NAMES[role]}</span></div>
    <div class="header-actions">
      <button type="button" class="header-action bell" disabled={busy}
        aria-label={notificationCount > 0 ? `Ειδοποιήσεις: ${notificationCount} μη αναγνωσμένες` : 'Ειδοποιήσεις'}
        onclick={() => { roleMenuOpen = false; void onNotifications(); }} data-testid="mobile-notifications">
        <MobileIcon name="bell" size={23} />
        {#if notificationCount > 0}<span class="notification-count" aria-hidden="true">{notificationCount > 99 ? '99+' : notificationCount}</span>{/if}
      </button>
      <button type="button" class="profile-action" disabled={busy} aria-label="Το προφίλ μου"
        onclick={() => { roleMenuOpen = false; void onProfile(); }} data-testid="mobile-profile">
        {#if avatarUrl && !avatarFailed}<img src={avatarUrl} alt="" onerror={() => { avatarFailed = true; }} />{:else}<span aria-hidden="true">{initial}</span>{/if}
      </button>
    </div>
    {#if roleMenuOpen}
      <div bind:this={roleMenu} class="role-menu" id={`${id}-roles`}>
        <p>Σύνδεση ως</p>
        {#each roles as option (option)}
          <button type="button" aria-pressed={option === role} disabled={busy} onkeydown={closeRoles}
            onclick={() => { roleMenuOpen = false; void onRoleChange(option); }} data-testid={`switch-role-${option}`}>
            <span>{ROLE_NAMES[option]}</span>{#if option === role}<MobileIcon name="check" size={18} />{:else}<MobileIcon name="arrow" size={18} />{/if}
          </button>
        {/each}
      </div>
    {/if}
  </header>

  <main class="mobile-content" id={`${id}-content`} tabindex="-1">{@render children()}</main>

  <nav class="bottom-nav" aria-label="Κύρια πλοήγηση" data-testid="bottom-nav">
    <div class="bottom-items">
      {#each tabs as tab, index (tab)}
        {#if hasComposer && index === 2}
          <button class="create-post" type="button" aria-label="Δημιουργία ανάρτησης" disabled={busy} onclick={() => void onCreatePost()} data-testid="mobile-create-post">
            <span><MobileIcon name="plus" size={29} /></span>
          </button>
        {/if}
        <button type="button" class:active={activeTab === tab} aria-current={activeTab === tab ? 'page' : undefined}
          disabled={busy} onclick={() => void onTabChange(tab)} data-testid={`bottom-tab-${tab}`}>
          <MobileIcon name={iconForTab(tab)} size={24} /><span>{TAB_NAMES[tab]}</span>
        </button>
      {/each}
    </div>
  </nav>
</div>

<style>
  .mobile-shell { width:100%; min-height:100dvh; background:radial-gradient(ellipse at 25% 0%,#bd8a2410,transparent 35%),var(--lf-bg); }
  .mobile-header { position:sticky; top:0; z-index:50; display:flex; align-items:center; gap:9px; height:calc(72px + env(safe-area-inset-top,0px)); padding:calc(8px + env(safe-area-inset-top,0px)) 12px 8px; border-bottom:1px solid var(--lf-line); background:#080a0bf5; backdrop-filter:blur(12px); }
  .header-action { display:grid; place-items:center; flex:0 0 auto; width:40px; height:44px; padding:0; border:0; background:transparent; color:#f5f5f5; cursor:pointer; }
  .header-action:hover:not(:disabled) { color:var(--lf-gold); }
  .club-crest { flex:0 0 auto; width:42px; height:48px; object-fit:contain; }
  .brand-lockup { min-width:0; flex:1; }
  .brand-lockup p { margin:0; color:#fff; font-family:var(--lf-heading); font-size:24px; font-weight:700; line-height:1.05; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .brand-lockup > span { display:block; margin-top:4px; color:#b4b8bb; font-family:var(--lf-heading); font-size:12px; letter-spacing:.08em; line-height:1.1; text-transform:uppercase; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .header-actions { display:flex; align-items:center; flex:0 0 auto; gap:3px; margin-left:auto; }
  .bell { position:relative; }
  .notification-count { position:absolute; top:0; right:1px; min-width:17px; height:17px; padding:0 4px; border-radius:50%; color:#080808; background:var(--lf-gold-light); font-size:10px; font-weight:700; line-height:17px; }
  .profile-action { display:grid; place-items:center; flex:0 0 auto; width:39px; height:39px; padding:0; overflow:hidden; border:1px solid var(--lf-gold); border-radius:50%; background:#151718; color:#fff; cursor:pointer; }
  .profile-action img { width:100%; height:100%; object-fit:cover; }
  .profile-action span { font-size:15px; }
  .role-menu { position:absolute; z-index:70; top:calc(68px + env(safe-area-inset-top,0px)); left:12px; width:min(290px,calc(100vw - 24px)); padding:8px; border:1px solid #54401c; border-radius:9px; background:#111415; box-shadow:0 16px 45px #000b; }
  .role-menu > p { margin:4px 9px 8px; color:var(--lf-muted); font-size:11px; text-transform:uppercase; letter-spacing:.08em; }
  .role-menu button { display:flex; align-items:center; justify-content:space-between; width:100%; min-height:46px; padding:10px; border:0; border-radius:5px; background:transparent; color:#d9dcde; text-align:left; cursor:pointer; }
  .role-menu button[aria-pressed='true'] { color:var(--lf-gold-light); background:#f5a62310; }
  .role-menu button:hover:not(:disabled) { background:#ffffff09; }
  .mobile-content { display:block; width:100%; min-height:calc(100dvh - 140px); margin:0; padding:20px 16px calc(92px + env(safe-area-inset-bottom,0px)); outline:none; }
  .bottom-nav { position:fixed; z-index:50; bottom:0; left:0; right:0; padding-bottom:env(safe-area-inset-bottom,0px); border-top:1px solid var(--lf-line); background:#070909f7; backdrop-filter:blur(14px); }
  .bottom-items { display:grid; grid-template-columns:repeat(5,minmax(0,1fr)); align-items:center; min-height:68px; padding:0 5px; }
  .bottom-items > button { display:flex; min-width:0; min-height:68px; flex-direction:column; align-items:center; justify-content:center; gap:5px; padding:8px 2px 6px; border:0; background:transparent; color:#e5e8e9; cursor:pointer; }
  .bottom-items > button > span { max-width:100%; font-family:var(--lf-heading); font-size:13px; line-height:1.15; }
  .bottom-items > button.active { color:var(--lf-gold-light); }
  .bottom-items > button:hover:not(:disabled) { color:var(--lf-gold); }
  .bottom-items > .create-post { gap:0; }
  .create-post > span { display:grid; place-items:center; width:48px; height:48px; border-radius:50%; background:linear-gradient(125deg,#ffcc43,#f5a623); color:#080808; box-shadow:0 1px 0 #ffd565 inset; }
  .skip-link { position:fixed; z-index:100; left:12px; top:-100px; padding:10px 14px; border-radius:5px; background:var(--lf-gold); color:#080808; font-weight:600; }
  .skip-link:focus { top:8px; }
  @media (max-width:360px) { .mobile-header{gap:6px;padding-left:7px;padding-right:8px}.header-action{width:34px}.brand-lockup p{font-size:22px}.club-crest{width:36px;height:44px}.header-actions{gap:0}.profile-action{width:35px;height:35px}.bottom-items > button > span{font-size:12px} }
</style>
