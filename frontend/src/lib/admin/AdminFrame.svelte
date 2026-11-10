<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { WebAdminSession } from './controller.ts';
  import { canReadClub } from './club-controller.ts';
  import { canViewFirstTeamTrials } from './trials-access.ts';
  import { adminSectionsFor } from './module-catalog.ts';
  import { ADMIN_ROLE_NAMES, isAdminRole } from './permissions.ts';

  let { user, active = 'account', busy = false, onLogout, children }: {
    user: WebAdminSession['user']; active?: 'account' | 'club' | 'news' | 'pos' | 'trials' | 'groups' | 'evaluations' | 'announcements' | 'onboarding'; busy?: boolean;
    onLogout: () => void; children: Snippet;
  } = $props();
  const roleName = $derived(isAdminRole(user.role) ? ADMIN_ROLE_NAMES[user.role] : '—');
  const sections = $derived(adminSectionsFor(user));
</script>

<div class="admin-workspace">
  <a class="admin-skip" href="#admin-main">Μετάβαση στο περιεχόμενο</a>
  <header class="admin-header">
    <a class="admin-brand" href="/admin" aria-label="Λευτεριά FC · Κέντρο διαχείρισης">
      <img src="/mobile/club-logo.png" alt="" width="42" height="48" />
      <span>ΛΕΥΤΕΡΙΑ FC <small>ΚΕΝΤΡΟ ΔΙΑΧΕΙΡΙΣΗΣ</small></span>
    </a>
    <div class="admin-actions">
      <a class="admin-public-link" href="/">Ιστοσελίδα <span aria-hidden="true">↗</span></a>
      <button type="button" class="admin-secondary" disabled={busy} onclick={onLogout} data-testid="admin-logout">Αποσύνδεση</button>
    </div>
  </header>
  <div class="admin-workspace-body">
    <nav class="admin-sidebar" aria-label="Διαχείριση συλλόγου" data-testid="admin-structured-navigation">
      <p class="admin-nav-label">ΠΙΝΑΚΑΣ ΕΛΕΓΧΟΥ</p>
      <a href="/admin" aria-current={active === 'account' ? 'page' : undefined}>Επισκόπηση</a>
      {#each sections as section (section.id)}
        <div class="admin-nav-group">
          <p class="admin-nav-label">{section.name}</p>
          <a class="admin-nav-section" href={'/admin#workspace-' + section.id}>{section.name} <span aria-hidden="true">›</span></a>
          {#if section.id === 'website' && canReadClub(user)}
            <a class="admin-nav-tool" href="/admin/club" aria-current={active === 'club' ? 'page' : undefined} data-testid="admin-club-link">Προφίλ συλλόγου</a>
          {/if}
          {#if section.id === 'website' && section.modules.some(item => item.id === 'news' && item.status === 'live')}
            <a class="admin-nav-tool" href="/admin/news" aria-current={active === 'news' ? 'page' : undefined} data-testid="admin-news-link">Άρθρα & νέα</a>
          {/if}
          {#if section.id === 'app' && section.modules.some(item => item.id === 'announcements' && item.status === 'live')}
            <a class="admin-nav-tool" href="/admin/announcements" aria-current={active === 'announcements' ? 'page' : undefined} data-testid="admin-announcements-link">Πρόχειρες ανακοινώσεις</a>
          {/if}
          {#if section.id === 'pos' && section.modules.some(item => item.id === 'products' && item.status === 'live')}
            <a class="admin-nav-tool" href="/admin/pos" aria-current={active === 'pos' ? 'page' : undefined} data-testid="admin-pos-link">Κατάλογος προϊόντων</a>
          {/if}
          {#if section.id === 'operations' && section.modules.some(item => item.id === 'onboarding' && item.status === 'live')}
            <a class="admin-nav-tool" href="/admin/onboarding" aria-current={active === 'onboarding' ? 'page' : undefined} data-testid="admin-onboarding-link">Οδηγός πρώτης χρήσης</a>
          {/if}
          {#if section.id === 'football' && section.modules.some(item => item.id === 'groups' && item.status === 'live')}
            <a class="admin-nav-tool" href="/admin/groups" aria-current={active === 'groups' ? 'page' : undefined} data-testid="admin-groups-link">Ομάδες & τμήματα</a>
          {/if}
          {#if section.id === 'football' && section.modules.some(item => item.id === 'evaluations' && item.status === 'live')}
            <a class="admin-nav-tool" href="/admin/evaluations" aria-current={active === 'evaluations' ? 'page' : undefined}>Αξιολογήσεις Ακαδημίας</a>
          {/if}
          {#if section.id === 'football' && canViewFirstTeamTrials(user)}
            <a class="admin-nav-tool" href="/admin/trials" aria-current={active === 'trials' ? 'page' : undefined} data-testid="admin-trials-link">Δοκιμαστικά πρώτης ομάδας</a>
          {/if}
        </div>
      {/each}
      <div class="admin-nav-group">
        <p class="admin-nav-label">ΛΟΓΑΡΙΑΣΜΟΣ</p>
        <a href="/admin#account">Στοιχεία</a>
        <a href="/admin#session">Σύνδεση</a>
        <a href="/admin#access">Δικαιώματα πρόσβασης</a>
      </div>
      <div class="admin-sidebar-context">
        <span>ΣΥΝΔΕΔΕΜΕΝΟΣ ΩΣ</span><strong>{user.name || user.username}</strong><small>{roleName}</small>
      </div>
    </nav>
    <main class="admin-content" id="admin-main" tabindex="-1">{@render children()}</main>
  </div>
</div>
