<script lang="ts">
  import type {WebAdminSession} from './controller.ts';
  import {adminSectionsFor} from './module-catalog.ts';
  import {footballNavigationFor} from './football-navigation.ts';
  import {hasAdminPermission} from './permissions.ts';
  let {user,active}:{user:WebAdminSession['user'];active:'registrations'|'leads'|'academy_players'|'first_team_players'}=$props();
  const live=$derived(new Set(adminSectionsFor(user).flatMap(s=>s.modules).filter(m=>m.status==='live').map(m=>m.id)));
  const football=$derived(footballNavigationFor(user));
  const links=$derived([
    {id:'registrations',label:'Εγγραφές',href:'/admin/academy-office',allowed:live.has('registrations')},
    {id:'academy_players',label:'Παίκτες Ακαδημίας',href:'/admin/groups?organization_id=lefteria_fc_academy&view=players',allowed:live.has('groups')&&football.academyGroups&&hasAdminPermission(user.permissions,'academy:write')},
    {id:'first_team_players',label:'Παίκτες Πρώτης Ομάδας',href:'/admin/groups?organization_id=lefteria_fc&view=players',allowed:live.has('groups')&&football.firstTeamGroups&&user.role==='super_admin'&&hasAdminPermission(user.permissions,'teams:write')},
    {id:'leads',label:'Ενδιαφερόμενοι',href:'/admin/leads',allowed:live.has('leads')}
  ].filter(link=>link.allowed));
</script>
{#if links.length>1}
  <nav class="crm-workspace-tabs" aria-label="CRM · Παίκτες & εγγραφές" data-testid="admin-unified-crm-tabs">
    {#each links as link (link.id)}
      <a href={link.href} data-sveltekit-reload aria-current={active===link.id?'page':undefined}>{link.label}</a>
    {/each}
  </nav>
{/if}
<style>
.crm-workspace-tabs{display:flex;flex-wrap:wrap;gap:6px;border-bottom:1px solid var(--admin-line);padding:10px 0;margin:6px 0 16px}
.crm-workspace-tabs a{display:inline-flex;align-items:center;min-height:44px;padding:8px 14px;border:1px solid var(--admin-line);border-radius:7px;text-decoration:none;color:inherit;font-weight:600}
.crm-workspace-tabs a[aria-current=page]{color:var(--admin-gold);border-color:var(--admin-gold)}
.crm-workspace-tabs a:focus-visible{outline:2px solid var(--admin-gold);outline-offset:2px}
</style>
