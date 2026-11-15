<script lang="ts">
  import type { WebAdminSession } from './controller.ts';
  import { hasAdminPermission } from './permissions.ts';
  import { adminSectionsFor } from './module-catalog.ts';
  import AdminFrame from './AdminFrame.svelte';
  import AdminClubMetrics from './AdminClubMetrics.svelte';
  import { adminOrganizationsFor } from './admin-organization.ts';

  let { session, busy = false, onLogout }: {
    session: WebAdminSession; busy?: boolean; onLogout: () => void;
  } = $props();
  const user = $derived(session.user);
  const sections = $derived(adminSectionsFor(user));
  const canSeeOverviewMetrics = $derived(
    ['teams:read','academy:read','news:read'].some(permission =>
      hasAdminPermission(user.permissions,permission)) ||
    ['manager','super_admin'].includes(user.role) &&
      hasAdminPermission(user.permissions,'marketing:read') &&
      user.organizations.some(org => org.id==='lefteria_fc_academy')
  );
  const quickLinks = $derived(sections.flatMap(section => section.modules)
    .filter(module => module.status === 'live' && !!module.href)
    .slice(0, 8)
    .map(module => ({ id: module.id, href: module.href!, name: module.name })));
</script>

<AdminFrame {user} {busy} {onLogout}>
  <div class="admin-workspace-intro">
    <p class="admin-eyebrow">ΛΕΥΤΕΡΙΑ FC · ΚΕΝΤΡΟ ΔΙΑΧΕΙΡΙΣΗΣ</p>
    <h1>Καλώς ήρθατε,<br />{user.name || user.username}</h1>
    <p class="admin-muted">Όλες οι ενότητες του συλλόγου, οργανωμένες ανά λειτουργία. Οι διαθέσιμες επιλογές εξαρτώνται από τα επαληθευμένα δικαιώματά σας.</p>
  </div>

  {#if canSeeOverviewMetrics}
    {#each adminOrganizationsFor(user) as organization (organization)}
      <AdminClubMetrics {user} {organization} />
    {/each}
  {/if}

  {#if quickLinks.length}
    <nav class="admin-panel admin-quick-navigation" aria-label="Γρήγορη πρόσβαση" data-testid="admin-quick-navigation">
      <h2>Γρήγορη πρόσβαση</h2>
      <div class="admin-quick-navigation-grid">
        {#each quickLinks as link (link.id)}
          <a href={link.href}>{link.name}<span aria-hidden="true">↗</span></a>
        {/each}
      </div>
    </nav>
  {/if}

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
                {:else if module.status === 'live' && module.id === 'payabl'}
                  <a class="admin-tool admin-tool-live" href="/admin/finance" data-testid="admin-payabl-module">
                    <span class="admin-tool-copy"><strong>{module.name}</strong><small>{module.description}</small></span>
                    <span class="admin-tool-arrow" aria-hidden="true">↗</span>
                  </a>
                {:else if module.status === 'live' && module.id === 'onboarding'}
                  <a class="admin-tool admin-tool-live" href="/admin/onboarding" data-testid="admin-onboarding-module">
                    <span class="admin-tool-copy"><strong>{module.name}</strong><small>{module.description}</small></span>
                    <span class="admin-tool-arrow" aria-hidden="true">↗</span>
                  </a>
                {:else if module.status === 'live' && module.id === 'hub_editorial'}
                  <a class="admin-tool admin-tool-live" href="/admin/hub" data-testid="admin-hub-editorial-module">
                    <span class="admin-tool-copy"><strong>{module.name}</strong><small>{module.description}</small></span>
                    <span class="admin-tool-arrow" aria-hidden="true">↗</span>
                  </a>
                {:else if module.status === 'live' && module.id === 'announcements'}
                  <a class="admin-tool admin-tool-live" href="/admin/announcements" data-testid="admin-announcements-module">
                    <span class="admin-tool-copy"><strong>{module.name}</strong><small>{module.description}</small></span>
                    <span class="admin-tool-arrow" aria-hidden="true">↗</span>
                  </a>
                {:else if module.status === 'live' && module.id === 'territories'}
                  <a class="admin-tool admin-tool-live" href="/admin/territories" data-testid="admin-territories-module">
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
                {:else if module.status === 'live' && module.href}
                  <a class="admin-tool admin-tool-live" href={module.href} data-testid={'admin-' + module.id + '-module'}>
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
      <p class="admin-muted">Ο λογαριασμός σας δεν έχει ενεργή ανάθεση διαχείρισης. Μπορείτε να δείτε την πρόσβασή σας στο προφίλ σας.</p>
    </section>
  {/if}

</AdminFrame>
