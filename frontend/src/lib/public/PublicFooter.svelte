<script lang="ts">
  import type { PublicClub } from './types';
  import PublicImage from './PublicImage.svelte';
  import Icon from './PublicIcon.svelte';
  import type { PublicIconName } from './icon-types';
  let { club }: { club: PublicClub | null } = $props();
  const links = [['home','Αρχική','/'],['about','Ο σύλλογος','/about'],['team','Πρώτη ομάδα','/team'],['academy','Ακαδημία','/academy'],['matches','Αγώνες','/team?tab=results'],['news','Νέα','/news'],['events','Εκδηλώσεις','/events'],['shop','Κατάστημα','/shop'],['contact','Επικοινωνία','/contact'],['registration','Εγγραφές','/academy/registration'],['seasons','Παλαιότερες σεζόν','/seasons'],['partners','Συνεργάτες','/partners']];
  const accounts: [string,string,string,PublicIconName][] = [['profile','Το προφίλ μου','/login','user'],['orders','Οι παραγγελίες μου','/profile','bag'],['tickets','Εισιτήρια','/shop','bag'],['cart','Το καλάθι μου','/cart','bag']];
  const socials: [string,string,PublicIconName][] = [['facebook','Facebook','facebook'],['instagram','Instagram','instagram'],['youtube','YouTube','youtube'],['twitter','X','external'],['tiktok','TikTok','external']];
  const name = $derived(club?.name || 'ΛΕΥΤΕΡΙΑ FC');
  function top() { window.scrollTo({ top: 0, behavior: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth' }); }
</script>
<footer class="public-footer" data-testid="footer">
  <div class="public-city-banner" style:background-image="url(https://images.unsplash.com/photo-1661155100640-37747341ad48?auto=format&fit=crop&w=1800&q=85)" data-testid="public-city-banner"><strong>ΛΕΜΕΣΟΣ</strong><p>Η ΠΟΛΗ ΜΑΣ.<br />Η ΟΙΚΟΓΕΝΕΙΑ ΜΑΣ.<br />ΤΟ ΜΕΛΛΟΝ ΜΑΣ.</p></div>
  <div class="public-footer-grid">
    <section class="public-footer-about"><a href="/" class="public-footer-brand" data-testid="footer-logo"><PublicImage src={club?.logo_url} alt={name} /><div><strong>{name}</strong><span>ΙΔΡ. {club?.founded || '2024'}</span></div></a>
      <p data-testid="footer-description">{club?.description || 'Αφοσιωμένοι στην αριστεία εντός και εκτός γηπέδου. Χτίζουμε πρωταθλητές μέσα από πάθος, πειθαρχία και ομαδικότητα.'}</p>
      {#if club?.address}<p data-testid="footer-address"><Icon name="pin" size={14} />{club.address}</p>{/if}
      {#if club?.email}<a href={`mailto:${club.email}`} data-testid="footer-email"><Icon name="mail" size={14} />{club.email}</a>{/if}
      {#if club?.phone}<a href={`tel:${club.phone}`} data-testid="footer-phone"><Icon name="phone" size={14} />{club.phone}</a>{/if}
    </section>
    <section><h2>ΓΡΗΓΟΡΟΙ ΣΥΝΔΕΣΜΟΙ</h2><nav class="public-footer-links">{#each links as [id,label,path]}<a href={path} data-testid={`footer-nav-${id}`}>{label}</a>{/each}</nav></section>
    <section><h2>Ο ΛΟΓΑΡΙΑΣΜΟΣ ΜΟΥ</h2><div class="public-footer-account">{#each accounts as [id,label,path,icon]}<a href={path} data-testid={`footer-account-${id}`}><Icon name={icon} size={17} />{label}</a>{/each}<a href="/register" data-testid="footer-customer-register">Δημιουργία λογαριασμού</a></div></section>
    <section><h2>ΑΚΟΛΟΥΘΗΣΕ ΜΑΣ</h2>
      {#if Object.keys(club?.social_links ?? {}).length}<div class="public-footer-social-group"><p>Λευτεριά FC</p><div>{#each socials as [key,label,icon]}{#if club?.social_links[key]}<a href={club.social_links[key]} target="_blank" rel="noreferrer" aria-label={`Λευτεριά FC · ${label}`} title={label} data-testid={`footer-${key}`}><Icon name={icon} size={21} /></a>{/if}{/each}</div></div>{/if}
      <button class="public-back-top" type="button" onclick={top} aria-label="Επιστροφή στην κορυφή" title="Επιστροφή στην κορυφή" data-testid="public-back-top"><Icon name="up" size={22} /></button>
    </section>
  </div>
  <div class="public-footer-app"><div class="public-app-download" data-testid="footer-app-download"><h4>Download Our Official App</h4><p>One official Lefteria FC app for iPhone, iPad, Android, and desktop.</p><a href="/app" class="public-app-open"><Icon name="download" size={21} />Άνοιξε την εφαρμογή στο browser</a></div></div>
  <div class="public-footer-bottom"><a href="/privacy" data-testid="footer-privacy-link">Πολιτική απορρήτου</a><a href="/terms" data-testid="footer-terms-link">Όροι χρήσης</a><a href="/admin/login" data-testid="footer-admin-link">Διαχείριση συλλόγου</a><span>© {club?.founded || '2024'} ΛΕΥΤΕΡΙΑ FC. Με επιφύλαξη κάθε δικαιώματος.</span></div>
</footer>
