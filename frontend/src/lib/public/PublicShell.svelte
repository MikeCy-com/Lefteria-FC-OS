<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { PublicGroup, PublicOrganization, PublicSite } from './types';
  import PublicImage from './PublicImage.svelte';
  import './public.css';
  let { site, organization = 'lefteria_fc', navigationGroups = [], children }: { site: PublicSite | null; organization?: PublicOrganization; navigationGroups?: PublicGroup[]; children: Snippet } = $props();
  let menu: HTMLDialogElement;
  const links = [ ['/', 'ΑΡΧΙΚΗ'], ['/about', 'Ο ΣΥΛΛΟΓΟΣ'], ['/team', 'ΠΡΩΤΗ ΟΜΑΔΑ'], ['/academy', 'ΑΚΑΔΗΜΙΑ'], ['/news', 'ΝΕΑ'], ['/contact', 'ΕΠΙΚΟΙΝΩΝΙΑ'] ];
  const club = $derived(site?.club ?? null);
  const name = $derived(club?.greek_name || club?.name || 'ΛΕΥΤΕΡΙΑ FC');
  function closeMenu() { menu?.close(); }
</script>

<div class="public-site" lang="el" data-organization={organization} data-testid="public-site">
  <a class="public-skip" href="#public-main">Μετάβαση στο περιεχόμενο</a>
  <aside class="public-rail" aria-label="Κύρια πλοήγηση">
    <a href="/" class="rail-crest" aria-label="Λευτεριά FC · Αρχική"><PublicImage src={club?.logo_url} alt="Λευτεριά FC" eager /></a>
    <nav>{#each links as [href, label]}<a {href}>{label}</a>{/each}</nav>
    {#if club?.founded}<span class="rail-founded">ΙΔΡ. {club.founded}</span>{/if}
  </aside>
  <header class="public-header">
    <button class="menu-trigger" aria-label="Άνοιγμα μενού" aria-haspopup="dialog" onclick={() => menu.showModal()}><span></span><span></span><span></span></button>
    <a class="public-wordmark" href="/"><PublicImage src={club?.logo_url} alt="" eager /><span>{name}</span></a>
    <div class="public-header-actions"><a href="/app">ΕΦΑΡΜΟΓΗ</a><a href="/admin" class="admin-link">ΔΙΑΧΕΙΡΙΣΗ <span aria-hidden="true">↗</span></a></div>
  </header>
  <dialog bind:this={menu} class="public-menu" aria-labelledby="public-menu-heading">
    <div class="menu-heading"><h2 id="public-menu-heading">{name}</h2><button aria-label="Κλείσιμο μενού" onclick={closeMenu}>×</button></div>
    <nav aria-label="Όλες οι σελίδες">{#each links as [href, label]}<a {href} onclick={closeMenu}>{label}<span aria-hidden="true">↗</span></a>{/each}</nav>
    {#if navigationGroups.length}<div class="public-menu-groups"><h3>ΗΛΙΚΙΑΚΑ ΤΜΗΜΑΤΑ</h3>{#each navigationGroups as group}<a href={`/academy#group-${encodeURIComponent(group.id)}`} onclick={closeMenu}>{group.name}</a>{/each}</div>{/if}
    <a class="public-button" href="/app" onclick={closeMenu}>Η ΕΦΑΡΜΟΓΗ ΤΟΥ ΣΥΛΛΟΓΟΥ <span aria-hidden="true">↗</span></a>
  </dialog>
  <div class="public-content"><main id="public-main" tabindex="-1">{@render children()}</main>
    <footer class="public-footer">
      <div class="footer-statement"><p>{club?.city || 'ΛΕΜΕΣΟΣ'}</p><h2>Η ΠΟΛΗ ΜΑΣ.<br />Η ΟΙΚΟΓΕΝΕΙΑ ΜΑΣ.<br /><em>ΤΟ ΜΕΛΛΟΝ ΜΑΣ.</em></h2></div>
      <div class="footer-columns"><section><a class="footer-brand" href="/"><PublicImage src={club?.logo_url} alt="" /><strong>{name}</strong></a>{#if club?.description}<p>{club.description}</p>{/if}</section>
        <section><h3>Ο ΣΥΛΛΟΓΟΣ</h3><nav>{#each links.slice(1) as [href, label]}<a {href}>{label}</a>{/each}</nav></section>
        <section><h3>ΕΠΙΚΟΙΝΩΝΙΑ</h3>{#if club?.address}<p>{club.address}</p>{/if}{#if club?.email}<a href={`mailto:${club.email}`}>{club.email}</a>{/if}{#if club?.phone}<a href={`tel:${club.phone}`}>{club.phone}</a>{/if}
          <div class="social-links">{#each Object.entries(club?.social_links ?? {}) as [label, href]}<a {href} target="_blank" rel="noreferrer">{label.toLocaleUpperCase('el-GR')} ↗</a>{/each}</div>
        </section>
      </div>
      <div class="footer-bottom"><span>© {new Date().getFullYear()} {name}</span><a href="/admin">ΔΙΑΧΕΙΡΙΣΗ ΣΥΛΛΟΓΟΥ</a><a href="/app">ΕΦΑΡΜΟΓΗ ΜΕΛΩΝ ↗</a></div>
    </footer>
  </div>
</div>
