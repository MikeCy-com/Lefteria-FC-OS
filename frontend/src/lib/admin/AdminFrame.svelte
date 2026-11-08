<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { WebAdminSession } from './controller.ts';
  import { canReadClub } from './club-controller.ts';
  import { canViewFirstTeamTrials } from './trials-access.ts';
  import { ADMIN_ROLE_NAMES, isAdminRole } from './permissions.ts';
  let { user, active = 'account', busy = false, onLogout, children }: {
    user: WebAdminSession['user']; active?: 'account' | 'club' | 'trials'; busy?: boolean;
    onLogout: () => void; children: Snippet;
  } = $props();
  const roleName = $derived(isAdminRole(user.role) ? ADMIN_ROLE_NAMES[user.role] : '—');
</script>

<div class="admin-workspace">
  <a class="admin-skip" href="#admin-main">Μετάβαση στο περιεχόμενο</a>
  <header class="admin-header">
    <a class="admin-brand" href="/admin"><img src="/mobile/club-logo.png" alt="" width="40" height="40" /><span>LEFTERIA FC OS <small>Διαχείριση</small></span></a>
    <div class="admin-actions"><a href="/">Ιστοσελίδα ↗</a><button type="button" class="admin-secondary" disabled={busy} onclick={onLogout} data-testid="admin-logout">Αποσύνδεση</button></div>
  </header>
  <div class="admin-workspace-body">
    <nav class="admin-sidebar" aria-label="Διαχείριση συλλόγου">
      <p class="admin-nav-label">Ο ΧΩΡΟΣ ΣΟΥ</p>
      <a href="/admin" aria-current={active === 'account' ? 'page' : undefined}>Λογαριασμός</a>
      <a href="/admin#session">Σύνδεση</a><a href="/admin#access">Δικαιώματα πρόσβασης</a>
      {#if canReadClub(user)}<p class="admin-nav-label">ΙΣΤΟΣΕΛΙΔΑ</p><a href="/admin/club" aria-current={active === 'club' ? 'page' : undefined} data-testid="admin-club-link">Προφίλ συλλόγου</a>{/if}
      {#if canViewFirstTeamTrials(user)}<a href="/admin/trials" aria-current={active === 'trials' ? 'page' : undefined} data-testid="admin-trials-link">Δοκιμαστικά πρώτης ομάδας</a>{/if}
      <div class="admin-sidebar-context"><span>ΣΥΝΔΕΔΕΜΕΝΟΣ ΩΣ</span><strong>{user.name || user.username}</strong><small>{roleName}</small></div>
    </nav>
    <main class="admin-content" id="admin-main" tabindex="-1">{@render children()}</main>
  </div>
</div>
