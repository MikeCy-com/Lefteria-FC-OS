<script lang="ts">
  import MobileShell from './MobileShell.svelte';
  import { GUEST_PUBLIC_LINKS, guestLoginHref, guestPublicLink } from '../guest-navigation.ts';
  import { TAB_NAMES, type MobileTab } from '../navigation.ts';
  import type { ClubBrand } from '../club-branding.ts';
  import AppInstallButton from '../../public/AppInstallButton.svelte';
  import { goto } from '$app/navigation';

  let { activeTab, brand }: { activeTab: MobileTab; brand: ClubBrand | null } = $props();
  const destination = $derived(guestPublicLink(activeTab));
  const restrictedTabs = ['profile', 'notifications', 'hub', 'gamehub', 'potm'] as const;

  async function openTab(tab: MobileTab) {
    const link = guestPublicLink(tab);
    if (link) {
      await goto(link.href);
      return;
    }
    if (tab === 'home' || tab === 'more') {
      await goto(tab === 'home' ? '/app' : '/app?tab=more');
      return;
    }
    await goto(guestLoginHref(tab));
  }
</script>

<!-- Guest navigation is deliberately separate from every authenticated
     dashboard and does not receive a mobile auth controller or user object. -->
<MobileShell guest role="fan" {activeTab} availableRoles={[]} userName="?"
  clubName={brand?.name ?? 'ΛΕΥΤΕΡΙΑ FC'} clubLogoUrl={brand?.logoUrl ?? '/mobile/club-logo.png'}
  onTabChange={openTab} onRoleChange={() => openTab('profile')}
  onNotifications={() => openTab('notifications')} onProfile={() => openTab('profile')}
  onCreatePost={() => openTab('profile')}>
  <div class="guest-content" data-testid="guest-home">
    {#if activeTab === 'home' || activeTab === 'more'}
      <section class="guest-welcome" aria-labelledby="guest-heading">
        <p class="eyebrow">ONE TEAM ● ONE DREAM</p>
        <h1 id="guest-heading">{activeTab === 'home' ? 'Καλώς ήρθες στη Λευτεριά FC' : 'Περισσότερα'}</h1>
        <p>Περιηγήσου ελεύθερα ως επισκέπτης. Για το προσωπικό σου προφίλ και τις υπηρεσίες μελών, συνδέσου με τον αριθμό τηλεφώνου σου.</p>
        <a class="sign-in" href={guestLoginHref('home')} data-testid="guest-login">Σύνδεση / Εγγραφή</a>
      </section>

      <section class="guest-section" aria-labelledby="guest-public-heading">
        <h2 id="guest-public-heading">Ο σύλλογός μας</h2>
        <div class="guest-links">
          {#each GUEST_PUBLIC_LINKS as link (link.tab)}
            <a href={link.href} data-testid={'guest-public-'+link.tab}>{link.label}<span aria-hidden="true">›</span></a>
          {/each}
          <a href="/academy">Ακαδημία<span aria-hidden="true">›</span></a>
          <a href="/events">Εκδηλώσεις<span aria-hidden="true">›</span></a>
          <a href="/about">Ο σύλλογος<span aria-hidden="true">›</span></a>
          <a href="/contact">Επικοινωνία<span aria-hidden="true">›</span></a>
        </div>
      </section>

      <section class="guest-section" aria-labelledby="guest-member-heading">
        <h2 id="guest-member-heading">Για μέλη & φιλάθλους</h2>
        <p>Απαιτείται επαληθευμένος λογαριασμός.</p>
        <div class="guest-links">
          {#each restrictedTabs as tab (tab)}
            <a href={guestLoginHref(tab)} data-testid={'guest-restricted-'+tab}>{TAB_NAMES[tab]}<span aria-hidden="true">›</span></a>
          {/each}
        </div>
      </section>

      <section class="guest-section" aria-labelledby="guest-install-heading">
        <h2 id="guest-install-heading">Η εφαρμογή στο κινητό σου</h2>
        <AppInstallButton />
      </section>
    {:else if destination}
      <section class="guest-section">
        <h1>{destination.label}</h1>
        <p>Δες το δημοσιευμένο περιεχόμενο του συλλόγου χωρίς σύνδεση.</p>
        <a class="sign-in" href={destination.href}>Άνοιγμα: {destination.label}</a>
        {#if activeTab==='potm'}<a class="back" href={guestLoginHref('potm')} data-testid="guest-potm-sign-in">Σύνδεση για ψηφοφορία</a>{/if}
        <a class="back" href="/app">Επιστροφή στην εφαρμογή</a>
      </section>
    {/if}
  </div>
</MobileShell>

<style>
  .guest-content {max-width:960px;margin:0 auto;display:grid;gap:14px;color:var(--lf-text,#fff)}
  .guest-welcome,.guest-section {padding:18px;border:1px solid #343331;border-radius:10px;background:linear-gradient(145deg,#191a1b,#101112)}
  .guest-welcome {border-color:#695227;background:linear-gradient(145deg,#2c2416,#101112 70%)}
  .guest-content h1 {font:700 26px/1.1 'Fira Sans Extra Condensed',sans-serif;margin:0 0 10px}
  .guest-content h2 {font:700 20px/1.15 'Fira Sans Extra Condensed',sans-serif;margin:0 0 12px}
  .guest-content p {color:#bfc2c5;font-size:13px;line-height:1.55;margin:0 0 14px}
  .guest-welcome .eyebrow {color:#f6bb38;font-size:11px;letter-spacing:.08em;font-weight:700}
  .guest-links {display:grid;gap:8px;grid-template-columns:repeat(auto-fit,minmax(min(100%,180px),1fr))}
  .guest-links a,.sign-in,.back {display:flex;align-items:center;justify-content:space-between;gap:12px;min-height:44px;padding:12px;border:1px solid #414244;border-radius:6px;color:#fff;text-decoration:none;font-size:14px}
  .guest-links a span {color:#f6bb38;font-size:18px}
  .sign-in {display:inline-flex;background:#f6bb38;color:#080808;border-color:#f6bb38;font-weight:700}
  .back {margin-top:12px}
  a:focus-visible {outline:2px solid #f6bb38;outline-offset:3px}
</style>
