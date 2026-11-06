<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicIcon from '../../../lib/public/PublicIcon.svelte';
  import type { PublicIconName } from '../../../lib/public/icon-types';
  import type { PublicPageData } from '../../../lib/public/types';

  let { data }: { data: PublicPageData } = $props();

  // Source-parity content: Lefteria-FC/frontend/src/pages/AcademyPhilosophyPage.jsx.
  const values: { icon: PublicIconName; title: string; description: string }[] = [
    { icon: 'heart', title: 'ΠΑΘΟΣ', description: 'Καλλιεργουμε την αγαπη για το ποδοσφαιρο. Καθε προπονηση ειναι μια ευκαιρια να ζησεις τη χαρα του αθληματος.' },
    { icon: 'shield', title: 'ΠΕΙΘΑΡΧΙΑ', description: 'Μαθαινουμε στους νεους αθλητες τη σημασια της δεσμευσης, της συνεπειας και του σεβασμου.' },
    { icon: 'users', title: 'ΟΜΑΔΙΚΟΤΗΤΑ', description: 'Το ποδοσφαιρο ειναι ομαδικο αθλημα. Χτιζουμε δεσμους, εμπιστοσυνη και συνεργασια.' },
    { icon: 'target', title: 'ΑΝΑΠΤΥΞΗ', description: 'Καθε παικτης εχει ατομικο πλανο αναπτυξης. Εστιαζουμε στη βελτιωση, οχι μονο στο αποτελεσμα.' },
    { icon: 'star', title: 'ΑΡΙΣΤΕΙΑ', description: 'Στοχευουμε στο υψηλοτερο επιπεδο σε ο,τι κανουμε. Απο την προπονηση μεχρι τη συμπεριφορα.' },
    { icon: 'zap', title: 'ΚΑΙΝΟΤΟΜΙΑ', description: 'Χρησιμοποιουμε συγχρονες μεθοδους προπονησης και τεχνολογια για την αναπτυξη των αθλητων.' }
  ];

  const approaches = [
    { title: 'Εξατομικευμενη Αναπτυξη', description: 'Καθε παικτης αξιολογειται ξεχωριστα. Δημιουργουμε ατομικα πλανα αναπτυξης με βαση τις αναγκες και τα δυνατα σημεια του καθε αθλητη.' },
    { title: 'Δομημενο Προγραμμα', description: 'Ακολουθουμε ενα επιστημονικα τεκμηριωμενο προγραμμα προπονησης που εξελισσεται αναλογα με την ηλικια και το επιπεδο καθε ομαδας.' },
    { title: 'Ασφαλες Περιβαλλον', description: 'Η ασφαλεια και η ευημερια των αθλητων μας ειναι η πρωτη μας προτεραιοτητα. Καθε παιδι πρεπει να νιωθει ασφαλες.' },
    { title: 'Συνεργασια με Γονεις', description: 'Οι γονεις ειναι αναποσπαστο μερος του ταξιδιου. Παρεχουμε τακτικη ενημερωση, αξιολογησεις και ανοιχτη επικοινωνια.' }
  ];
</script>

<svelte:head>
  <title>Η ΦΙΛΟΣΟΦΙΑ ΜΑΣ | ΑΚΑΔΗΜΙΑ ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content="Οι αξίες και η προπονητική προσέγγιση της Ακαδημίας Λευτεριά FC." />
</svelte:head>

<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups}>
  <div class="academy-philosophy" data-testid="philosophy-page">
    <section class="philosophy-hero" aria-labelledby="philosophy-heading">
      <div class="philosophy-container">
        <p class="philosophy-eyebrow">Ακαδημια Λευτεριά FC</p>
        <h1 id="philosophy-heading" class="philosophy-title">
          Η ΦΙΛΟΣΟΦΙΑ <span>ΜΑΣ</span>
        </h1>
        <p class="philosophy-lead">
          Πιστευουμε οτι καθε νεος αθλητης αξιζει ενα περιβαλλον που τον αναπτυσσει ολοπλευρα — τεχνικα, τακτικα, σωματικα και ψυχολογικα.
        </p>
      </div>
    </section>
    <div class="philosophy-container"><div class="philosophy-divider" aria-hidden="true"></div></div>

    <section class="philosophy-section" aria-labelledby="philosophy-values-heading">
      <div class="philosophy-container">
        <h2 id="philosophy-values-heading" class="philosophy-section-title">ΟΙ ΑΞΙΕΣ <span>ΜΑΣ</span></h2>
        <div class="philosophy-accent-bar" aria-hidden="true"></div>
        <div class="philosophy-values">
          {#each values as value, i (value.title)}
            <article class="philosophy-card" data-testid={'value-' + i}>
              <div class="philosophy-card-icon"><PublicIcon name={value.icon} size={24} /></div>
              <h3 class="philosophy-card-title">{value.title}</h3>
              <p class="philosophy-card-description">{value.description}</p>
            </article>
          {/each}
        </div>
      </div>
    </section>
    <div class="philosophy-container"><div class="philosophy-divider" aria-hidden="true"></div></div>

    <section class="philosophy-section" aria-labelledby="philosophy-approach-heading" data-testid="approach-section">
      <div class="philosophy-container">
        <h2 id="philosophy-approach-heading" class="philosophy-section-title">Η ΠΡΟΣΕΓΓΙΣΗ <span>ΜΑΣ</span></h2>
        <div class="philosophy-accent-bar" aria-hidden="true"></div>
        <div class="philosophy-approaches">
          {#each approaches as approach, i (approach.title)}
            <article class="philosophy-card philosophy-approach-card">
              <span class="philosophy-step" aria-hidden="true">{i + 1}</span>
              <div>
                <h3 class="philosophy-card-title">{approach.title}</h3>
                <p class="philosophy-card-description">{approach.description}</p>
              </div>
            </article>
          {/each}
        </div>
      </div>
    </section>
  </div>
</PublicShell>

<style>
  .academy-philosophy { background: #080808; min-height: 100vh; }
  .philosophy-container { max-width: 1400px; margin-inline: auto; padding-inline: 24px; }
  .philosophy-hero { padding: 76px 0 64px; }
  .philosophy-eyebrow { display: inline-block; margin: 0 0 18px; padding: 7px 11px; color: #f5a623; background: #f5a62316; border: 1px solid #f5a62342; font-size: 12px; font-weight: 700; text-transform: uppercase; }
  .philosophy-title { margin: 0; font-size: clamp(48px, 6.3vw, 72px); line-height: 1.05; letter-spacing: .01em; }
  .philosophy-title span, .philosophy-section-title span { color: #f5a623; }
  .philosophy-lead { max-width: 672px; margin: 24px 0 0; color: #a1a1aa; font-size: 18px; line-height: 1.65; }
  .philosophy-divider { height: 1px; background: #262626; }
  .philosophy-section { padding: 64px 0; }
  .philosophy-section-title { margin: 0 0 12px; font-size: clamp(30px, 3.5vw, 40px); line-height: 1.1; }
  .philosophy-accent-bar { width: 48px; height: 4px; margin: 0 0 40px; background: #f5a623; }
  .philosophy-values { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 20px; }
  .philosophy-approaches { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 20px; }
  .philosophy-card { min-width: 0; padding: 24px; background: #111; border: 1px solid #262626; }
  .philosophy-card-icon { display: flex; margin-bottom: 16px; color: #f5a623; }
  .philosophy-card-title { margin: 0 0 10px; font-size: 24px; line-height: 1.15; color: #fff; }
  .philosophy-card-description { margin: 0; color: #8a8a92; font-size: 14px; line-height: 1.7; }
  .philosophy-approach-card { display: flex; gap: 20px; align-items: flex-start; }
  .philosophy-approach-card .philosophy-card-title { font-size: 21px; }
  .philosophy-step { flex: 0 0 40px; display: grid; place-items: center; width: 40px; height: 40px; color: #f5a623; background: #f5a62316; font-family: 'Fira Sans Extra Condensed', sans-serif; font-weight: 700; font-size: 21px; }
  @media (max-width: 1023px) { .philosophy-values { grid-template-columns: repeat(2, minmax(0, 1fr)); } }
  @media (max-width: 767px) {
    .philosophy-container { padding-inline: 18px; }
    .philosophy-hero { padding: 44px 0 48px; }
    .philosophy-section { padding: 48px 0; }
    .philosophy-values, .philosophy-approaches { grid-template-columns: 1fr; gap: 14px; }
    .philosophy-lead { font-size: 16px; }
    .philosophy-card { padding: 20px; }
    .philosophy-approach-card { gap: 15px; }
  }
</style>
