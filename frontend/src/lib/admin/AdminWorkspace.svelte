<script lang="ts">
  import type { WebAdminSession } from './controller.ts';
  import { ADMIN_ROLE_NAMES, adminCapabilityName, isAdminRole } from './permissions.ts';
  import { adminSectionsFor } from './module-catalog.ts';
  import AdminFrame from './AdminFrame.svelte';

  let { session, busy = false, onLogout, onRefresh }: {
    session: WebAdminSession; busy?: boolean; onLogout: () => void; onRefresh: () => void;
  } = $props();
  const user = $derived(session.user);
  const sections = $derived(adminSectionsFor(user));
  const liveCount = $derived(sections.reduce((n, group) => n + group.modules.filter(item => item.status === 'live').length, 0));
  const plannedCount = $derived(sections.reduce((n, group) => n + group.modules.filter(item => item.status === 'planned').length, 0));
  const roleName = $derived(isAdminRole(user.role) ? ADMIN_ROLE_NAMES[user.role] : '—');
  const expiry = $derived(new Intl.DateTimeFormat('el-GR', {
    dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Nicosia'
  }).format(new Date(session.expires_at)));
</script>

<AdminFrame {user} {busy} {onLogout}>
  <div class="admin-workspace-intro">
    <p class="admin-eyebrow">ΛΕΥΤΕΡΙΑ FC · ΚΕΝΤΡΟ ΔΙΑΧΕΙΡΙΣΗΣ</p>
    <h1>Καλώς ήρθατε,<br />{user.name || user.username}</h1>
    <p class="admin-muted">Όλες οι ενότητες του συλλόγου, οργανωμένες ανά λειτουργία. Οι διαθέσιμες επιλογές εξαρτώνται από τα επαληθευμένα δικαιώματά σας.</p>
  </div>

  <div class="admin-overview-stats" aria-label="Επισκόπηση διαθέσιμων εργαλείων" data-testid="admin-overview">
    <div><span>Ενεργά εργαλεία</span><strong>{liveCount}</strong><small>Επαληθευμένες υπηρεσίες</small></div>
    <div><span>Υπό υλοποίηση</span><strong>{plannedCount}</strong><small>Δεν δέχονται ακόμη ενέργειες</small></div>
    <div><span>Οργανισμοί</span><strong>{user.organizations.length}</strong><small>Τρέχουσες αναθέσεις</small></div>
    <div><span>Ο ρόλος σας</span><strong class="admin-stat-role">{roleName}</strong><small>Δικαιώματα σύνδεσης</small></div>
  </div>

  {#if sections.length}
    <div class="admin-hub-grid" data-testid="admin-module-sections">
      {#each sections as section, index (section.id)}
        <section class="admin-panel admin-hub-section" id={'workspace-' + section.id}
          aria-labelledby={'workspace-heading-' + section.id} data-testid={'admin-section-' + section.id}>
          <header class="admin-hub-heading">
            <span class="admin-section-number" aria-hidden="true">{String(index + 1).padStart(2, '0')}</span>
            <div>
              <p class="admin-eyebrow">ΔΙΑΧΕΙΡΙΣΗ · {section.name}</p>
              <h2 id={'workspace-heading-' + section.id}>{section.name}</h2>
              <p class="admin-muted">{section.description}</p>
            </div>
          </header>
          <ul class="admin-module-list">
            {#each section.modules as module (module.id)}
              <li>
                {#if module.status === 'live' && module.id === 'club'}
                  <a class="admin-tool admin-tool-live" href="/admin/club">
                    <span class="admin-tool-copy"><strong>{module.name}</strong><small>{module.description}</small></span>
                    <span class="admin-tool-arrow" aria-hidden="true">↗</span>
                  </a>
                {:else if module.status === 'live' && module.id === 'news'}
                  <a class="admin-tool admin-tool-live" href="/admin/news" data-testid="admin-news-module">
                    <span class="admin-tool-copy"><strong>{module.name}</strong><small>{module.description}</small></span>
                    <span class="admin-tool-arrow" aria-hidden="true">↗</span>
                  </a>
                {:else if module.status === 'live' && module.id === 'products'}
                  <a class="admin-tool admin-tool-live" href="/admin/pos" data-testid="admin-pos-module">
                    <span class="admin-tool-copy"><strong>{module.name}</strong><small>{module.description}</small></span>
                    <span class="admin-tool-arrow" aria-hidden="true">↗</span>
                  </a>
                {:else if module.status === 'live' && module.id === 'groups'}
                  <a class="admin-tool admin-tool-live" href="/admin/groups" data-testid="admin-groups-module">
                    <span class="admin-tool-copy"><strong>{module.name}</strong><small>{module.description}</small></span>
                    <span class="admin-tool-arrow" aria-hidden="true">↗</span>
                  </a>
                {:else if module.status === 'live' && module.id === 'trials'}
                  <a class="admin-tool admin-tool-live" href="/admin/trials" data-testid="admin-trials-module">
                    <span class="admin-tool-copy"><strong>{module.name}</strong><small>{module.description}</small></span>
                    <span class="admin-tool-arrow" aria-hidden="true">↗</span>
                  </a>
                {:else}
                  <div class="admin-tool admin-tool-planned" data-testid={'admin-pending-' + module.id}>
                    <span class="admin-tool-copy"><strong>{module.name}</strong><small>{module.description}</small></span>
                    <span class="admin-development">Under Developments</span>
                  </div>
                {/if}
              </li>
            {/each}
          </ul>
        </section>
      {/each}
    </div>
  {:else}
    <section class="admin-panel" data-testid="admin-no-modules">
      <h2>Δεν υπάρχουν διαθέσιμες ενότητες</h2>
      <p class="admin-muted">Ο λογαριασμός σας δεν έχει ενεργή ανάθεση διαχείρισης. Μπορείτε να δείτε την πρόσβασή σας παρακάτω.</p>
    </section>
  {/if}

  <div class="admin-account-grid">
    <section class="admin-panel" id="account" aria-labelledby="account-title">
      <p class="admin-eyebrow">Ο ΛΟΓΑΡΙΑΣΜΟΣ ΣΑΣ</p>
      <h2 id="account-title">Στοιχεία λογαριασμού</h2>
      <dl class="admin-details">
        <div><dt>Όνομα</dt><dd>{user.name || '—'}</dd></div>
        <div><dt>Όνομα χρήστη</dt><dd>{user.username}</dd></div>
        <div><dt>Ρόλος</dt><dd data-testid="admin-role">{roleName}</dd></div>
      </dl>
    </section>
    <section class="admin-panel" id="session" aria-labelledby="session-title">
      <p class="admin-eyebrow">ΑΣΦΑΛΕΙΑ</p>
      <h2 id="session-title">Τρέχουσα σύνδεση</h2>
      <p class="admin-muted">Η σύνδεση λήγει στις <time datetime={session.expires_at}>{expiry}</time> (ώρα Κύπρου).</p>
      <button type="button" class="admin-secondary" disabled={busy} onclick={onRefresh}>Ανανέωση στοιχείων πρόσβασης</button>
    </section>
  </div>
  <section class="admin-panel" id="access" aria-labelledby="access-title">
    <p class="admin-eyebrow">ΠΡΟΣΒΑΣΗ</p><h2 id="access-title">Δικαιώματα & οργανισμοί</h2>
    {#if user.organizations.length}
      <ul class="admin-tags" data-testid="admin-organizations">
        {#each user.organizations as organization (organization.id)}<li>{organization.name}</li>{/each}
      </ul>
      <p class="admin-muted">Η πρόσβαση ελέγχεται ξανά από το API για κάθε προστατευμένη ενέργεια.</p>
    {:else}
      <p class="admin-muted">Δεν έχει ανατεθεί οργανισμός σε αυτόν τον λογαριασμό.</p>
    {/if}
    <h3>Δυνατότητες ρόλου</h3>
    <ul class="admin-permissions" data-testid="admin-permissions">
      {#each user.permissions as permission}<li>{adminCapabilityName(permission)}</li>{/each}
    </ul>
  </section>
</AdminFrame>
