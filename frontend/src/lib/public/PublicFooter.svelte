<script lang="ts">
  import type { PublicClub } from './types';
  import PublicImage from './PublicImage.svelte';
  import Icon from './PublicIcon.svelte';
  import AppInstallButton from './AppInstallButton.svelte';
  import type { PublicIconName } from './icon-types';
  let { club }: { club: PublicClub | null } = $props();
  const links = [['home','Αρχική','/'],['about','Ο σύλλογος','/about'],['team','Πρώτη ομάδα','/team'],['academy','Ακαδημία','/academy'],['matches','Αγώνες','/team?tab=results'],['news','Νέα','/news'],['events','Εκδηλώσεις','/events'],['shop','Κατάστημα','/shop'],['contact','Επικοινωνία','/contact'],['registration','Εγγραφές','/academy/registration'],['seasons','Παλαιότερες σεζόν','/seasons'],['partners','Συνεργάτες','/partners']];
  const accounts: [string,string,string,PublicIconName][] = [['profile','Το προφίλ μου','/app/login','user'],['orders','Οι παραγγελίες μου','/profile','bag'],['tickets','Προϊόντα συλλόγου','/shop','bag'],['cart','Το καλάθι μου','/cart','bag']];
  const socials: [string,string,PublicIconName][] = [['facebook','Facebook','facebook'],['instagram','Instagram','instagram'],['youtube','YouTube','youtube'],['twitter','X','external'],['tiktok','TikTok','external']];
  const name = $derived(club?.name || 'ΛΕΥΤΕΡΙΑ FC');
  // Render only verified configured social destinations; no placeholder icons.
  const hasPublicSocials = $derived(socials.some(([key]) => Boolean(club?.social_links?.[key])));
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
    <section><h2>Ο ΛΟΓΑΡΙΑΣΜΟΣ ΜΟΥ</h2><div class="public-footer-account">{#each accounts as [id,label,path,icon]}<a href={path} target={id === 'profile' ? '_blank' : undefined} rel={id === 'profile' ? 'noopener noreferrer' : undefined} data-testid={`footer-account-${id}`}><Icon name={icon} size={17} />{label}</a>{/each}<a href="/register" data-testid="footer-customer-register">Δημιουργία λογαριασμού</a></div></section>
    <section class="public-footer-follow" data-testid="footer-follow"><h2>{hasPublicSocials ? 'ΑΚΟΛΟΥΘΗΣΕ ΜΑΣ' : 'ΜΕΙΝΕ ΕΝΗΜΕΡΟΣ'}</h2>
      {#if hasPublicSocials}<div class="public-footer-social-group"><p>Λευτεριά FC</p><div>{#each socials as [key,label,icon]}{#if club?.social_links[key]}<a href={club.social_links[key]} target="_blank" rel="noreferrer" aria-label={`Λευτεριά FC · ${label}`} title={label} data-testid={`footer-${key}`}><Icon name={icon} size={21} /></a>{/if}{/each}</div></div>{:else}<div class="public-footer-follow-empty" data-testid="footer-social-fallback">
        <p>Ανακοινώσεις και εκδηλώσεις από τον επίσημο ιστότοπο του συλλόγου.</p>
        <a href="/news">ΤΕΛΕΥΤΑΙΑ ΝΕΑ <Icon name="arrow" size={15} /></a>
        <a href="/events">ΕΚΔΗΛΩΣΕΙΣ <Icon name="arrow" size={15} /></a>
      </div>{/if}
      <button class="public-back-top" type="button" onclick={top} aria-label="Επιστροφή στην κορυφή" title="Επιστροφή στην κορυφή" data-testid="public-back-top"><Icon name="up" size={22} /></button>
    </section>
  </div>
  <div class="public-footer-app"><div class="public-app-download" data-testid="footer-app-download"><div class="public-app-download-copy"><p class="public-eyebrow">ΛΕΥΤΕΡΙΑ FC · ΕΦΑΡΜΟΓΗ ΜΕΛΩΝ</p><h4>Η ΕΠΙΣΗΜΗ ΜΑΣ ΕΦΑΡΜΟΓΗ</h4><p>Γρήγορη πρόσβαση στον σύλλογο από κινητό, tablet ή υπολογιστή, σύμφωνα με τον ρόλο σου.</p></div><AppInstallButton /></div></div>
  <div class="public-footer-bottom"><a href="/privacy" data-testid="footer-privacy-link">Πολιτική απορρήτου</a><a href="/terms" data-testid="footer-terms-link">Όροι χρήσης</a><span>© {club?.founded || '2024'} ΛΕΥΤΕΡΙΑ FC. Με επιφύλαξη κάθε δικαιώματος.</span></div>
</footer>
