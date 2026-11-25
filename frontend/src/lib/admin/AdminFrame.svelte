<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { WebAdminSession } from './controller.ts';
  import AdminProfilePhoto from './AdminProfilePhoto.svelte';
  import { canViewFirstTeamTrials } from './trials-access.ts';
  import { adminSectionsFor } from './module-catalog.ts';
  import { ADMIN_ROLE_NAMES,isAdminRole,hasAdminPermission } from './permissions.ts';

  let {user,active='account',busy=false,onLogout,children}:{
    user:WebAdminSession['user'];
    active?:'drills'|'account'|'profile'|'settings'|'club'|'news'|'pos'|'trials'|'groups'|'announcements'|'communications'|
      'onboarding'|'leads'|'finance'|'attendance'|'evaluations'|'documents'|'academy-office'|
      'schedule'|'hub'|'hub-subscriptions'|'matches'|'staff'|'users'|'territories'|'pages'|'media'|'sponsors';
    busy?:boolean;onLogout:()=>void;children:Snippet;
  }=$props();
  const roleName=$derived(isAdminRole(user.role)?ADMIN_ROLE_NAMES[user.role]:'—');
  const sections=$derived(adminSectionsFor(user));
  const liveIds=$derived(new Set(sections.flatMap(section=>section.modules)
    .filter(module=>module.status==='live').map(module=>module.id)));
  const initials=$derived((user.name || user.username).trim().split(/\s+/u)
    .slice(0,2).map(part=>part.slice(0,1).toLocaleUpperCase('el-GR')).join('') || 'FC');
  const hasGroupHub=$derived(liveIds.has('groups'));
  const canManagePlayerCrm=$derived(hasGroupHub &&
    ['manager','super_admin'].includes(user.role) &&
    (hasAdminPermission(user.permissions,'academy:write') ||
      hasAdminPermission(user.permissions,'teams:write')));
  // The club profile is reachable from Settings. An editorial menu is shown
  // only when it contains at least one real, authorized editorial destination.
  const hasWebsite=$derived(liveIds.has('pages')||liveIds.has('news')||liveIds.has('media')||liveIds.has('sponsors'));
</script>

<div class="admin-workspace">
  <a class="admin-skip" href="#admin-main">Μετάβαση στο περιεχόμενο</a>
  <header class="admin-header">
    <a class="admin-brand" href="/admin" aria-label="Λευτεριά FC · Κέντρο διαχείρισης">
      <img src="/mobile/club-logo.png" alt="" width="42" height="48"/>
      <span>ΛΕΥΤΕΡΙΑ FC <small>CMS</small></span>
    </a>
    <div class="admin-actions">
      <a class="admin-public-link" href="/" aria-label="Δημόσια ιστοσελίδα">Ιστοσελίδα <span aria-hidden="true">↗</span></a>
      <a class="admin-public-link" href="/app" aria-label="Εφαρμογή μελών">Εφαρμογή <span aria-hidden="true">↗</span></a>
      <details class="admin-profile-menu" data-testid="admin-profile-menu">
        <summary aria-label="Άνοιγμα προφίλ λογαριασμού" data-testid="admin-profile-menu-toggle">
          <AdminProfilePhoto actorId={user.id} {initials}/>
          <span class="admin-profile-menu-text"><strong>{user.name || user.username}</strong><small>{roleName}</small></span>
          <span aria-hidden="true">⌄</span>
        </summary>
        <div class="admin-profile-dropdown" role="group" aria-label="Λογαριασμός διαχειριστή">
          <p class="admin-profile-dropdown-name">{user.name || user.username}<small>{roleName}</small></p>
          <a href="/admin/profile" data-testid="admin-profile-link">Το προφίλ μου</a>
          <a href="/admin/profile#security">Ασφάλεια & σύνδεση</a>
          <a href="/admin/profile#access" data-testid="admin-my-access-link">Οι αναθέσεις & τα δικαιώματά μου</a>
          <a href="/admin/settings" data-testid="admin-settings-header-link">Ρυθμίσεις</a>
          <button type="button" disabled={busy} onclick={onLogout} data-testid="admin-logout">Αποσύνδεση</button>
        </div>
      </details>
    </div>
  </header>
  <div class="admin-workspace-body">
    <nav class="admin-sidebar" aria-label="Διαχείριση συλλόγου" data-testid="admin-structured-navigation">
      <!-- Only the work areas are visible by default. A native disclosure
           reveals one flat level of real, permission-scoped destinations.
           Group operations remain embedded inside the selected group. -->
      <a class="admin-nav-home" href="/admin" aria-current={active==='account'?'page':undefined}>Επισκόπηση</a>

      {#if liveIds.has('drills') || hasGroupHub || liveIds.has('territories') || canViewFirstTeamTrials(user)}
        <details class="admin-nav-disclosure"
          open={['drills','groups','schedule','attendance','evaluations','documents','matches','territories','trials'].includes(active)}
          data-testid="admin-football-navigation">
          <summary class="admin-nav-category" data-testid="admin-football-category">
            Ομάδες & Ακαδημία <span aria-hidden="true">⌄</span>
          </summary>
          <div class="admin-nav-links">
            {#if hasGroupHub}
              <a class="admin-nav-section" href="/admin/groups"
                aria-current={['groups','schedule','attendance','evaluations','documents','matches'].includes(active)?'page':undefined}
                data-testid="admin-groups-link">Ομάδες & τμήματα</a>
              {#if canManagePlayerCrm}
                <a class="admin-nav-tool" href="/admin/groups?view=players"
                  data-testid="admin-player-crm-link">Παίκτες CRM</a>
              {/if}
            {/if}
            {#if liveIds.has('drills')}
              <a class="admin-nav-tool" href="/admin/drills" aria-current={active==='drills'?'page':undefined} data-testid="admin-drills-link">Βιβλιοθήκη ασκήσεων</a>
            {/if}
            {#if liveIds.has('territories')}
              <a class="admin-nav-tool" href="/admin/territories" aria-current={active==='territories'?'page':undefined}
                data-testid="admin-territories-link">Πόλεις, τοποθεσίες & γήπεδα</a>
            {/if}
            {#if canViewFirstTeamTrials(user)}
              <a class="admin-nav-tool" href="/admin/trials" aria-current={active==='trials'?'page':undefined}
                data-testid="admin-trials-link">Δοκιμαστικά πρώτης ομάδας</a>
            {/if}
          </div>
        </details>
      {/if}

      {#if hasWebsite}
        <details class="admin-nav-disclosure"
          open={['pages','news','media','sponsors'].includes(active)}
          data-testid="admin-website-navigation">
          <summary class="admin-nav-category" data-testid="admin-website-category">
            Ιστοσελίδα <span aria-hidden="true">⌄</span>
          </summary>
          <div class="admin-nav-links">
            {#if liveIds.has('pages')}
              <a class="admin-nav-section" href="/admin/website-pages" aria-current={active==='pages'?'page':undefined}>
                Σελίδες & Visual Builder</a>
            {/if}
            {#if liveIds.has('news')}
              <a class="admin-nav-tool" href="/admin/news" aria-current={active==='news'?'page':undefined}
                data-testid="admin-news-link">Άρθρα & νέα</a>
            {/if}
            {#if liveIds.has('media')}
              <a class="admin-nav-tool" href="/admin/media" aria-current={active==='media'?'page':undefined}
                data-testid="admin-media-link">Εικόνες & αρχεία</a>
            {/if}
            {#if liveIds.has('sponsors')}
              <a class="admin-nav-tool" href="/admin/sponsors" aria-current={active==='sponsors'?'page':undefined}
                data-testid="admin-sponsors-link">Χορηγοί & συνεργάτες</a>
            {/if}
            <!-- Public club identity is configured in Settings, not repeated here. -->
          </div>
        </details>
      {/if}

      {#if liveIds.has('hub_editorial') || liveIds.has('hub_subscriptions') || liveIds.has('announcements') || liveIds.has('communications')}
        <details class="admin-nav-disclosure"
          open={['communications','announcements','hub','hub-subscriptions'].includes(active)}
          data-testid="admin-member-app-navigation">
          <summary class="admin-nav-category" data-testid="admin-member-app-category">
            Εφαρμογή & HUB <span aria-hidden="true">⌄</span>
          </summary>
          <div class="admin-nav-links">
            {#if liveIds.has('communications')}
              <a class="admin-nav-section" href="/admin/communications"
                aria-current={active==='communications'?'page':undefined}
                data-testid="admin-communications-link">Επικοινωνία</a>
            {/if}
            {#if liveIds.has('announcements')}
              <a class="admin-nav-tool" href="/admin/announcements"
                aria-current={active==='announcements'?'page':undefined}
                data-testid="admin-announcements-link">Ανακοινώσεις</a>
            {/if}
            {#if liveIds.has('hub_editorial')}
              <a class="admin-nav-tool" href="/admin/hub" aria-current={active==='hub'?'page':undefined}
                data-testid="admin-hub-link">Εκπαιδευτικό HUB & εγκρίσεις</a>
            {/if}
            {#if liveIds.has('hub_subscriptions')}
              <a class="admin-nav-tool" href="/admin/hub/subscriptions"
                aria-current={active==='hub-subscriptions'?'page':undefined}
                data-testid="admin-hub-subscriptions-link">Συνδρομές HUB</a>
            {/if}
          </div>
        </details>
      {/if}

      {#if liveIds.has('registrations') || liveIds.has('leads')}
        <details class="admin-nav-disclosure" open={['academy-office','leads'].includes(active)}
          data-testid="admin-crm-navigation">
          <summary class="admin-nav-category" data-testid="admin-crm-category">
            CRM & εγγραφές <span aria-hidden="true">⌄</span>
          </summary>
          <div class="admin-nav-links">
            {#if liveIds.has('registrations')}
              <a class="admin-nav-section" href="/admin/academy-office"
                aria-current={active==='academy-office'?'page':undefined}>Εγγραφές Ακαδημίας</a>
            {/if}
            {#if liveIds.has('leads')}
              <a class="admin-nav-tool" href="/admin/leads" aria-current={active==='leads'?'page':undefined}
                data-testid="admin-crm-leads-link">Ενδιαφερόμενοι</a>
            {/if}
          </div>
        </details>
      {/if}

      {#if liveIds.has('payabl') || liveIds.has('products') || liveIds.has('registers')}
        <details class="admin-nav-disclosure" open={['finance','pos'].includes(active)}
          data-testid="admin-commerce-navigation">
          <summary class="admin-nav-category" data-testid="admin-commerce-category">
            Οικονομικά & POS <span aria-hidden="true">⌄</span>
          </summary>
          <div class="admin-nav-links">
            {#if liveIds.has('payabl')}
              <a class="admin-nav-section" href="/admin/finance" aria-current={active==='finance'?'page':undefined}
                data-testid="admin-payabl-link">Χρεώσεις & εισπράξεις</a>
            {/if}
            {#if liveIds.has('products') || liveIds.has('registers')}
              <a class="admin-nav-tool" href="/admin/pos" aria-current={active==='pos'?'page':undefined}
                data-testid="admin-pos-link">{user.role === 'cashier' ? 'Το ταμείο μου' : 'Ταμεία, βάρδιες & προϊόντα'}</a>
            {/if}
          </div>
        </details>
      {/if}

      <!-- Staff, user grants, onboarding, infrastructure and organization
           configuration are reachable from the one permission-scoped Settings
           landing page, rather than duplicated in multiple nested menus. -->
      <a class="admin-nav-settings-home" href="/admin/settings"
        aria-current={['settings','club','staff','users','onboarding'].includes(active)?'page':undefined}
        data-testid="admin-settings-link">Ρυθμίσεις & πρόσβαση</a>
    </nav>
    <main class="admin-content" id="admin-main" tabindex="-1">{@render children()}</main>
  </div>
</div>
