<script lang="ts">
  import { tick, onDestroy } from 'svelte';
  import { page } from '$app/state';
  import { publicNavigation, categoryLinks, navigationActive, navigationLinkCurrent } from './navigation';
  import type { PublicClub, PublicGroup } from './types';
  import Icon from './PublicIcon.svelte';
  import PublicImage from './PublicImage.svelte';
  import SiteSearch from './SiteSearch.svelte';
  import { lockPageScroll } from './dialog-scroll';
  let { club, groups = [] }: { club: PublicClub | null; groups?: PublicGroup[] } = $props();
  let menu: HTMLDialogElement; let title: HTMLHeadingElement;
  let menuOpen = $state(false); let selectedCategory = $state<string | null>(null);
  let unlockScroll: (() => void) | null = null;
  const category = $derived(publicNavigation.find(item => item.id === selectedCategory));
  const items = $derived(category ? categoryLinks(category, groups) : []);
  const name = $derived(club?.name || 'ΛΕΥΤΕΡΙΑ FC');
  let route = '';
  $effect(() => { const next = page.url.pathname + page.url.search; if (next !== route) { route = next; close(false); selectedCategory = null; } });
  async function open(id: string | null = null) {
    selectedCategory = id; menuOpen = true;
    if (!menu.open) {
      const active = document.activeElement;
      returnFocus = active instanceof HTMLElement ? active : null;
      menu.showModal(); unlockScroll = lockPageScroll(document);
    }
    await tick(); title.focus();
  }
  // A native dialog restores focus in most browsers, but explicit restoration
  // prevents the drawer losing its keyboard trigger on narrow/desktop layouts.
  let returnFocus: HTMLElement | null = null;
  function restoreFocus() {
    const focus = returnFocus;
    returnFocus = null;
    if (focus?.isConnected) focus.focus({ preventScroll: true });
  }
  function releaseScroll() { unlockScroll?.(); unlockScroll = null; }
  function close(restore = true) {
    if (menu?.open) menu.close();
    menuOpen = false;
    releaseScroll();
    if (restore) restoreFocus();
    else returnFocus = null;
  }
  onDestroy(releaseScroll);
  function backdrop(event: MouseEvent) {
    if (event.target !== menu) return;
    const box = menu.getBoundingClientRect();
    if (event.clientX < box.left || event.clientX > box.right || event.clientY < box.top || event.clientY > box.bottom) close();
  }
</script>
<a href="#public-main" class="public-skip" data-testid="public-skip-content">Μετάβαση στο περιεχόμενο</a>
<aside class="public-rail" aria-label="Κύρια πλοήγηση" data-testid="public-sidebar">
  <a href="/" class="public-rail-logo" data-testid="nav-logo"><PublicImage src={club?.logo_url} alt={name} eager /></a>
  <nav>{#each publicNavigation as item (item.id)}
    {#if item.items}<button class:is-current={navigationActive(item, page.url)} type="button" aria-haspopup="dialog" aria-expanded={menuOpen && selectedCategory === item.id} onclick={() => void open(item.id)} data-testid={`public-rail-${item.id}`}><Icon name={item.icon} size={23} /><span>{item.label}</span></button>
    {:else}<a href={item.path} aria-current={navigationLinkCurrent(item.path, page.url, groups)} class:is-current={navigationActive(item, page.url)} data-testid={`public-rail-${item.id}`}><Icon name={item.icon} size={23} /><span>{item.label}</span></a>{/if}
  {/each}</nav><span class="public-rail-year">{club?.founded || '2024'}</span>
</aside>
<header class="public-header" data-testid="main-navigation">
  <button type="button" class="public-icon-button" aria-label="Άνοιγμα μενού" aria-haspopup="dialog" aria-expanded={menuOpen} onclick={() => void open()} data-testid="public-menu-open"><Icon name="menu" size={25} /></button>
  <a href="/" class="public-header-brand" data-testid="public-header-logo"><PublicImage src={club?.logo_url} alt="" eager /><div><strong>{name}</strong><span>ΙΔΡ. {club?.founded || '2024'}</span></div></a>
  <div class="public-header-tools"><SiteSearch {groups} />
    <button type="button" class="public-icon-button" disabled aria-label="Ειδοποιήσεις · προσωρινά μη διαθέσιμες" title="Οι ειδοποιήσεις δεν είναι ακόμη διαθέσιμες σε αυτή την προεπισκόπηση." data-testid="push-toggle"><Icon name="bell" size={19} /></button>
    <a href="/login" class="public-icon-button" aria-label="Σύνδεση" title="Σύνδεση" data-testid="public-account-link"><Icon name="user" size={21} /></a>
    <a href="/cart" class="public-icon-button public-cart" aria-label="Καλάθι" title="Καλάθι" data-testid="public-cart-link"><Icon name="bag" size={20} /></a>
    <a href="/shop" class="public-button public-ticket-link" data-testid="public-shop-link">ΚΑΤΑΣΤΗΜΑ <Icon name="arrow" size={18} /></a>
  </div>
</header>
<dialog bind:this={menu} class="public-dialog public-menu-dialog" aria-labelledby="public-menu-heading" aria-describedby="public-menu-description" onclose={() => { menuOpen = false; releaseScroll(); restoreFocus(); }} onclick={backdrop} data-testid="public-menu-drawer">
  <h2 bind:this={title} tabindex="-1" class="public-menu-title" id="public-menu-heading" data-testid="public-menu-heading"><PublicImage src={club?.logo_url} alt="" />{category?.label || 'ΛΕΥΤΕΡΙΑ FC'}</h2>
  <p class="sr-only" id="public-menu-description">{category ? `Σύνδεσμοι ενότητας ${category.label}` : 'Πλοήγηση σε όλες τις σελίδες του συλλόγου'}</p>
  <nav aria-label={category?.label || 'Όλες οι σελίδες'} data-testid="public-menu-navigation">
    {#if category}<button class="public-menu-back" type="button" onclick={() => void open()}><Icon name="back" size={16} />Όλες οι ενότητες</button>
      <div class="public-submenu public-submenu-only" data-testid={`public-submenu-${category.id}`}>{#each items as [id, label, path] (id)}<a href={path} aria-current={navigationLinkCurrent(path, page.url, groups)} onclick={() => close(false)} data-testid={`public-submenu-${category.id}-${id}`}>{label}<Icon name="arrow" size={14} /></a>{/each}</div>
    {:else}{#each publicNavigation as item (item.id)}<div class="public-menu-group">
      {#if item.items}<button type="button" onclick={() => void open(item.id)} class="public-menu-row" class:is-current={navigationActive(item, page.url)} data-testid={`public-menu-${item.id}`}><Icon name={item.icon} size={21} /><span>{item.label}</span><Icon name="chevron" size={18} /></button>
      {:else}<a href={item.path} aria-current={navigationLinkCurrent(item.path, page.url, groups)} onclick={() => close(false)} class="public-menu-row" class:is-current={navigationActive(item, page.url)} data-testid={`public-menu-${item.id}`}><Icon name={item.icon} size={21} /><span>{item.label}</span></a>{/if}
    </div>{/each}{/if}
  </nav>
  {#if !category}<div class="public-menu-account"><a href="/login" onclick={() => close(false)} data-testid="public-menu-account">Σύνδεση</a><a href="/academy/registration" onclick={() => close(false)} data-testid="public-menu-register">Εγγραφή αθλητή <Icon name="arrow" size={16} /></a><a href="/cart" onclick={() => close(false)} data-testid="public-menu-cart">Καλάθι</a><a href="/admin/login" onclick={() => close(false)} data-testid="public-menu-admin">Διαχείριση συλλόγου <Icon name="arrow" size={16} /></a></div>{/if}
  <button type="button" class="public-dialog-close" onclick={() => close()} aria-label="Κλείσιμο μενού" data-testid="public-menu-close"><Icon name="close" size={16} /></button>
</dialog>

<style>
  .public-submenu a[aria-current] { color:var(--public-gold); background:#f5a62312; box-shadow:inset 3px 0 var(--public-gold); padding-left:12px; }
</style>
