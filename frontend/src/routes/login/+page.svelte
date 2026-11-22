<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import { publicImages } from '../../lib/public/images.ts';
  import type { PublicPageData } from '../../lib/public/types';
  let { data }: { data: PublicPageData } = $props();
  const welcomeImage = $derived(data.navigationGroups.find(group => group.banner_url)?.banner_url || publicImages.hero);
</script>
<svelte:head><title>Σύνδεση | ΛΕΥΤΕΡΙΑ FC</title><meta name="robots" content="noindex" /></svelte:head>
<PublicShell site={data.site} branding={data.branding} navigationGroups={data.navigationGroups} organization="lefteria_fc">
  <div class="club-access">
    <section class="club-access-welcome" style:background-image={`linear-gradient(180deg,#080a0be8,#080a0b30 60%,#080a0b),url("${welcomeImage.replaceAll('"','%22')}")`}>
      <p class="public-eyebrow">Η ΟΙΚΟΓΕΝΕΙΑ ΤΗΣ ΛΕΥΤΕΡΙΑΣ</p>
      <h1>ΚΑΛΩΣ ΗΡΘΑΤΕ ΣΤΗ<br /><em>ΛΕΥΤΕΡΙΑ FC</em></h1>
      <p>Η ψηφιακή κοινότητα της ομάδας μας. Παίκτες, γονείς και φίλαθλοι, πιο κοντά στη Λευτεριά.</p>
    </section>
    <section class="club-access-panel" aria-labelledby="app-access-heading">
      <img src={data.branding?.logo_url || '/mobile/club-logo.png'} alt="" width="88" height="88" />
      <p class="public-eyebrow">ΕΠΙΣΗΜΗ ΕΦΑΡΜΟΓΗ</p>
      <h2 id="app-access-heading">Ο ΛΟΓΑΡΙΑΣΜΟΣ ΣΟΥ.<br /><span>Η ΟΜΑΔΑ ΣΟΥ.</span></h2>
      <p>Συνδέσου με τον αριθμό του κινητού σου. Οι διαθέσιμοι ρόλοι σου εμφανίζονται μετά την επαλήθευση.</p>
      <ul class="club-access-audiences" aria-label="Η κοινότητα της Λευτεριάς"><li>ΠΑΙΚΤΕΣ</li><li>ΓΟΝΕΙΣ</li><li>ΦΙΛΑΘΛΟΙ</li></ul>
      <a class="public-button club-access-action" href="/app/login">ΣΥΝΔΕΣΗ ΣΤΗΝ ΕΦΑΡΜΟΓΗ <span aria-hidden="true">→</span></a>
      <p class="club-access-note">Πρόγραμμα, ενημερώσεις και προσωπικές ρυθμίσεις, σύμφωνα με τον ρόλο σου.</p>
      <div class="club-access-family"><strong>Η ΟΙΚΟΓΕΝΕΙΑ ΣΟΥ</strong><p>Οι γονείς έχουν πρόσβαση στα παιδιά τους και στις ρυθμίσεις οικογένειας μέσα στην εφαρμογή.</p></div>
    </section>
  </div>
</PublicShell>
<style>
  .club-access { display:grid; grid-template-columns:minmax(0,1.15fr) minmax(0,1fr); gap:clamp(24px,4vw,64px); padding:32px clamp(20px,4vw,60px) 48px; background:radial-gradient(ellipse at top right,#f6bb3814,transparent 60%),#080a0b; align-items:center; }
  .club-access-welcome { min-height:640px; padding:clamp(24px,3vw,44px); background-size:cover; background-position:center; border-bottom:2px solid var(--public-gold); }
  .club-access-welcome h1 { font-size:clamp(38px,4.5vw,68px); line-height:1.03; font-style:italic; margin:20px 0; }
  .club-access-welcome em { color:var(--public-gold); }
  .club-access-welcome>p:last-child { max-width:42ch; font-size:16px; color:#e3e3df; }
  .club-access-panel { min-width:0; padding:clamp(24px,3vw,42px); border:1px solid #a18c5e; border-radius:12px; background:linear-gradient(145deg,#1b1c1c,#080a0b 75%); box-shadow:0 12px 40px #0006,inset 0 -2px #f6bb3860; }
  .club-access-panel>img { object-fit:contain; margin-bottom:24px; }
  .club-access-panel h2 { font-size:clamp(30px,3vw,44px); line-height:1.05; font-style:italic; margin-bottom:20px; }
  .club-access-panel h2 span { color:var(--public-gold); }
  .club-access-panel>p { color:#bfc0bb; font-size:14px; }
  .club-access-audiences { display:flex; flex-wrap:wrap; gap:8px; padding:0; margin:24px 0; list-style:none; }
  .club-access-audiences li { border:1px solid #8b703c; padding:10px 14px; color:var(--public-gold); font-family:'Fira Sans Extra Condensed',sans-serif; font-size:18px; font-weight:700; }
  .club-access-panel .club-access-action { width:100%; justify-content:space-between; min-height:54px; font-size:21px; background:linear-gradient(120deg,var(--public-gold),var(--public-gold)); color:#080808; border-radius:6px; }
  .club-access-note { margin-top:16px; }
  .club-access-family { border-top:1px solid #6a5835; margin-top:28px; padding-top:22px; }
  .club-access-family strong { font-family:'Fira Sans Extra Condensed',sans-serif; color:var(--public-gold); font-size:21px; }
  .club-access-family p { color:#bfc0bb; font-size:14px; margin-top:8px; }
  @media(max-width:900px) { .club-access { grid-template-columns:1fr; padding:20px 16px 36px; gap:20px; } .club-access-welcome { min-height:340px; background-position:center 35%; } .club-access-welcome h1 { font-size:42px; } }
  @media(max-width:380px) { .club-access-welcome h1 { font-size:34px; } .club-access-panel { padding:22px; } }
</style>
