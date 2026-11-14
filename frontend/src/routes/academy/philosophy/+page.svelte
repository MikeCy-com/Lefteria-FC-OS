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

