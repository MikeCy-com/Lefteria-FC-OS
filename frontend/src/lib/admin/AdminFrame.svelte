<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { WebAdminSession } from './controller.ts';
  import AdminProfilePhoto from './AdminProfilePhoto.svelte';
  import { canReadClub } from './club-controller.ts';
  import { canViewFirstTeamTrials } from './trials-access.ts';
  import { adminSectionsFor } from './module-catalog.ts';
  import { ADMIN_ROLE_NAMES,isAdminRole } from './permissions.ts';

  let {user,active='account',busy=false,onLogout,children}:{
    user:WebAdminSession['user'];
    active?:'account'|'profile'|'settings'|'club'|'news'|'pos'|'trials'|'groups'|'announcements'|'communications'|
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
  const hasOperations=$derived(liveIds.has('staff')||liveIds.has('users')||liveIds.has('onboarding'));
  const hasWebsite=$derived(liveIds.has('pages')||liveIds.has('news')||liveIds.has('media')||liveIds.has('sponsors')||canReadClub(user));
</script>

<div class="admin-workspace">
  <a class="admin-skip" href="#admin-main">Μετάβαση στο περιεχόμενο</a>
  <header class="admin-header">
    <a class="admin-brand" href="/admin" aria-label="Λευτεριά FC · Κέντρο διαχείρισης">
      <img src="/mobile/club-logo.png" alt="" width="42" height="48"/>
      <span>ΛΕΥΤΕΡΙΑ FC <small>ΚΕΝΤΡΟ ΔΙΑΧΕΙΡΙΣΗΣ</small></span>
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
      <p class="admin-nav-label">ΕΡΓΑΣΙΕΣ</p>
      <a class="admin-nav-section" href="/admin" aria-current={active==='account'?'page':undefined}>Επισκόπηση</a>
      {#if hasGroupHub || liveIds.has('territories') || canViewFirstTeamTrials(user)}
        <div class="admin-nav-group" data-testid="admin-football-navigation">
          <p class="admin-nav-label">ΟΜΑΔΕΣ & ΑΚΑΔΗΜΙΑ</p>
          {#if hasGroupHub}<a class="admin-nav-section" href="/admin/groups" aria-current={['groups','schedule','attendance','evaluations','documents','matches'].includes(active)?'page':undefined} data-testid="admin-groups-link">Ομάδες & τμήματα</a>{/if}
          {#if liveIds.has('territories')}<a class="admin-nav-tool" href="/admin/territories" aria-current={active==='territories'?'page':undefined} data-testid="admin-territories-link">Πόλεις & τοποθεσίες</a>{/if}
          {#if canViewFirstTeamTrials(user)}<a class="admin-nav-tool" href="/admin/trials" aria-current={active==='trials'?'page':undefined} data-testid="admin-trials-link">Δοκιμαστικά</a>{/if}
        </div>
      {/if}
      {#if hasWebsite}
        <div class="admin-nav-group">
          <p class="admin-nav-label">ΙΣΤΟΣΕΛΙΔΑ</p>
          {#if liveIds.has('pages')}<a class="admin-nav-section" href="/admin/website-pages" aria-current={active==='pages'?'page':undefined}>Σελίδες · Visual Builder</a>{/if}
          {#if liveIds.has('news')}<a class="admin-nav-section" href="/admin/news" aria-current={active==='news'?'page':undefined} data-testid="admin-news-link">Άρθρα & νέα</a>{/if}
          {#if liveIds.has('media')}<a class="admin-nav-tool" href="/admin/media" aria-current={active==='media'?'page':undefined} data-testid="admin-media-link">Βιβλιοθήκη εικόνων</a>{/if}
          {#if liveIds.has('sponsors')}<a class="admin-nav-tool" href="/admin/sponsors" aria-current={active==='sponsors'?'page':undefined} data-testid="admin-sponsors-link">Χορηγοί & συνεργάτες</a>{/if}
          {#if canReadClub(user)}<a class="admin-nav-tool" href="/admin/settings#website-settings" aria-current={active==='club'?'page':undefined}>Ρυθμίσεις ιστοσελίδας</a>{/if}
        </div>
      {/if}
      {#if liveIds.has('hub_editorial') || liveIds.has('hub_subscriptions') || liveIds.has('announcements') || liveIds.has('communications')}
        <div class="admin-nav-group">
          <p class="admin-nav-label">ΕΦΑΡΜΟΓΗ ΜΕΛΩΝ</p>
          {#if liveIds.has('communications')}<a class="admin-nav-section" href="/admin/communications" aria-current={active==='communications'?'page':undefined} data-testid="admin-communications-link">Επικοινωνία εφαρμογής</a>{/if}
          {#if liveIds.has('announcements')}<a class="admin-nav-tool" href="/admin/announcements" aria-current={active==='announcements'?'page':undefined} data-testid="admin-announcements-link">Ανακοινώσεις</a>{/if}
          {#if liveIds.has('hub_editorial')}<a class="admin-nav-tool" href="/admin/hub" aria-current={active==='hub'?'page':undefined} data-testid="admin-hub-link">HUB · Περιεχόμενο</a>{/if}
          {#if liveIds.has('hub_subscriptions')}<a class="admin-nav-tool" href="/admin/hub/subscriptions" aria-current={active==='hub-subscriptions'?'page':undefined} data-testid="admin-hub-subscriptions-link">HUB · Συνδρομές φιλάθλων</a>{/if}
        </div>
      {/if}
      {#if liveIds.has('registrations') || liveIds.has('leads')}
        <div class="admin-nav-group">
          <p class="admin-nav-label">CRM & ΕΠΑΦΕΣ</p>
          {#if liveIds.has('registrations')}<a class="admin-nav-section" href="/admin/academy-office" aria-current={active==='academy-office'?'page':undefined}>Εγγραφές Ακαδημίας</a>{/if}
          {#if liveIds.has('leads')}<a class="admin-nav-tool" href="/admin/leads" aria-current={active==='leads'?'page':undefined} data-testid="admin-crm-leads-link">Ενδιαφερόμενοι</a>{/if}
        </div>
      {/if}
      {#if liveIds.has('payabl') || liveIds.has('products')}
        <div class="admin-nav-group">
          <p class="admin-nav-label">ΟΙΚΟΝΟΜΙΚΑ & ΚΑΤΑΣΤΗΜΑ</p>
          {#if liveIds.has('payabl')}<a class="admin-nav-section" href="/admin/finance" aria-current={active==='finance'?'page':undefined} data-testid="admin-payabl-link">Οικονομικά</a>{/if}
          {#if liveIds.has('products')}<a class="admin-nav-tool" href="/admin/pos" aria-current={active==='pos'?'page':undefined} data-testid="admin-pos-link">POS & προϊόντα</a>{/if}
        </div>
      {/if}
      {#if hasOperations}
        <div class="admin-nav-group">
          <p class="admin-nav-label">ΠΡΟΣΩΠΙΚΟ & ΠΡΟΣΒΑΣΗ</p>
          {#if liveIds.has('staff')}<a class="admin-nav-section" href="/admin/staff" aria-current={active==='staff'?'page':undefined} data-testid="admin-staff-link">Προσωπικό & προπονητές</a>{/if}
          {#if liveIds.has('users')}<a class="admin-nav-tool" href="/admin/users" aria-current={active==='users'?'page':undefined} data-testid="admin-users-link">Χρήστες & δικαιώματα</a>{/if}
          {#if liveIds.has('onboarding')}<a class="admin-nav-tool" href="/admin/onboarding" aria-current={active==='onboarding'?'page':undefined}>Οδηγός διαχειριστή</a>{/if}
        </div>
      {/if}
      <div class="admin-nav-group admin-settings-section">
          <p class="admin-nav-label">ΣΥΣΤΗΜΑ</p>
          <a class="admin-nav-section" href="/admin/settings" aria-current={active==='settings'?'page':undefined} data-testid="admin-settings-link">Ρυθμίσεις</a>
          <details class="admin-nav-settings-details" open={active==='settings'||active==='profile'}
            data-testid="admin-settings-submenu">
            <summary>Κατηγορίες ρυθμίσεων <span aria-hidden="true">⌄</span></summary>
            <div class="admin-nav-settings-links">
              {#if hasWebsite}
                <a class="admin-nav-tool" href="/admin/settings#website-settings">Ιστοσελίδα &amp; περιεχόμενο</a>
              {/if}
              {#if liveIds.has('territories')}
                <a class="admin-nav-tool" href="/admin/settings#football-settings">Πόλεις, γήπεδα &amp; τοποθεσίες</a>
              {/if}
              {#if hasOperations}
                <a class="admin-nav-tool" href="/admin/settings#access-settings">Προσωπικό &amp; δικαιώματα</a>
              {/if}
            </div>
          </details>
        </div>
    </nav>
    <main class="admin-content" id="admin-main" tabindex="-1">{@render children()}</main>
  </div>
</div>
