<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import PublicIcon from '../../lib/public/PublicIcon.svelte';
  import type { PublicIconName } from '../../lib/public/icon-types';
  import type { PublicPageData } from '../../lib/public/types';
  let { data }: { data: PublicPageData & {aboutIntro: {heading:string;description:string}|null; aboutHistory: {heading:string;paragraphs:[string,string,string]}|null} } = $props();
  const values: {icon: PublicIconName;title:string;desc:string}[] = [{"title": "Αριστεία", "desc": "Επιδιώκουμε την αριστεία σε όλα όσα κάνουμε, εντός και εκτός γηπέδου.", "icon": "trophy"}, {"title": "Πάθος", "desc": "Το ποδόσφαιρο είναι περισσότερο από ένα παιχνίδι - είναι το πάθος και ο τρόπος ζωής μας.", "icon": "heart"}, {"title": "Φιλοδοξία", "desc": "Θέτουμε υψηλούς στόχους και εργαζόμαστε ακούραστα για να τους επιτύχουμε.", "icon": "target"}];
</script>
<svelte:head><title>Σχετικά με τη ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Η ιστορία, οι αξίες και η έδρα της Λευτεριάς FC." /></svelte:head>
<PublicShell site={data.site} organization={data.organization} navigationGroups={data.navigationGroups} branding={data.branding}>
  <div class="source-about pt-24 min-h-screen" data-testid="about-page">

    <section class="py-10 md:py-20 px-4 md:px-6 about-dark-surface">
      <div class="max-w-7xl mx-auto">
        <span class="badge badge-secondary mb-4">Η Ιστορια μας</span>
        <h1 class="text-4xl md:text-5xl about-copy-primary mb-6">
          {#if data.aboutIntro}{data.aboutIntro.heading}{:else}Σχετικα με την <span class="about-brand-accent">ΛΕΥΤΕΡΙΑ FC</span>{/if}
        </h1>
        <p class="text-xl about-copy-body max-w-3xl">
          {data.aboutIntro?.description ?? 'Ιδρύθηκε το 2024, η ΛΕΥΤΕΡΙΑ FC ενσαρκώνει το πνεύμα του πάθους, της αριστείας και της κοινότητας στο κυπριακό ποδόσφαιρο.'}
        </p>
      </div>
    </section>


    <section class="py-10 md:py-20 px-4 md:px-6">
      <div class="max-w-7xl mx-auto grid lg:grid-cols-2 gap-6 lg:gap-12 items-center">
        <div>
          <h2 class="text-4xl about-copy-primary mb-6 section-heading">{data.aboutHistory?.heading ?? 'Η Ιστορια μας'}</h2>
          <div class="space-y-4 about-copy-body">
            {#if data.aboutHistory}
              {#each data.aboutHistory.paragraphs as paragraph, index (index)}
                <p>{paragraph}</p>
              {/each}
            {:else}
            <p>
              Η ΛΕΥΤΕΡΙΑ FC ιδρύθηκε με όραμα να δημιουργήσει έναν ποδοσφαιρικό σύλλογο που συνδυάζει
              την επαγγελματική αριστεία με τις κοινοτικές αξίες. Το περιστέρι στο έμβλημά μας
              συμβολίζει την ελευθερία και την ειρήνη, ενώ το στεφάνι δάφνης αντιπροσωπεύει
              τη νίκη και το επίτευγμα.
            </p>
            <p>
              Με έδρα τη Λεμεσό της Κύπρου, αγωνιζόμαστε στον Α' Όμιλο του ΠΑΑΟΚ.
            </p>
            <p>
              Το όνομά μας "ΛΕΥΤΕΡΙΑ" σημαίνει "Ελευθερία" στα ελληνικά, αντιπροσωπεύοντας τη
              φιλοσοφία μας για ελεύθερο, επιθετικό ποδόσφαιρο και την ελευθερία που δίνουμε
              στους παίκτες μας να εκφραστούν.
            </p>
            {/if}
          </div>
        </div>
        <div class="relative">
          <PublicImage
            src={data.site?.club?.logo_url || "/club/logo512.png"}
            alt="ΛΕΥΤΕΡΙΑ FC Crest"
            class="w-full max-w-md mx-auto"
          />
        </div>
      </div>
    </section>


    <section class="py-10 md:py-20 px-4 md:px-6 about-dark-surface">
      <div class="max-w-7xl mx-auto">
        <h2 class="text-4xl about-copy-primary mb-12 text-center">Οι Αξιες μας</h2>
        <div class="grid md:grid-cols-3 gap-8">
          {#each values as value, i}
<div class="card p-8 text-center">
              <PublicIcon name={value.icon} size={48} />
              <h3 class="text-2xl about-copy-primary mb-3">{value.title}</h3>
              <p class="about-copy-dim">{value.desc}</p>
            </div>
{/each}
        </div>
      </div>
    </section>


    <section class="py-10 md:py-20 px-4 md:px-6">
      <div class="max-w-7xl mx-auto">
        <div class="grid lg:grid-cols-2 gap-6 lg:gap-12 items-center">
          <div>
            <span class="badge badge-secondary mb-4">Εδρα</span>
            <h2 class="text-4xl about-copy-primary mb-6 section-heading">Γηπεδο Αετου</h2>
            <div class="space-y-4 about-copy-body">
              <p>
                Η έδρα μας, το Γήπεδο Αετού, βρίσκεται στην καρδιά της Λεμεσού.
                Το γήπεδο διαθέτει σύγχρονες εγκαταστάσεις και φιλοξενεί τους
                εντός έδρας αγώνες της ομάδας μας.
              </p>
              <ul class="space-y-2">
                <li class="flex items-center gap-3">
                  <PublicIcon name="star" size={18} />
                  <span>Φυσικός χλοοτάπητας</span>
                </li>
                <li class="flex items-center gap-3">
                  <PublicIcon name="star" size={18} />
                  <span>Σύγχρονες προπονητικές εγκαταστάσεις</span>
                </li>
                <li class="flex items-center gap-3">
                  <PublicIcon name="star" size={18} />
                  <span>Αποδυτήρια υψηλών προδιαγραφών</span>
                </li>
                <li class="flex items-center gap-3">
                  <PublicIcon name="star" size={18} />
                  <span>Χώροι φιλοξενίας θεατών</span>
                </li>
              </ul>
            </div>
          </div>
          <div class="aspect-video about-image-surface rounded-none overflow-hidden">
            <PublicImage
              src="https://customer-assets.emergentagent.com/job_club-academy-portal/artifacts/vq6hm4ij_unnamed%20%282%29.webp"
              alt="Γήπεδο Αετού"
              class="w-full h-full object-cover"
            />
          </div>
        </div>
      </div>
    </section>
  </div>
</PublicShell>

<style>
  /* Legacy Tailwind paint classes previously bypassed published design colors.
     These semantic roles keep the source composition and original fallbacks. */
  .about-dark-surface { background:var(--lf-color-bg,#0a0a0a); }
  .about-image-surface { background:var(--lf-color-surface,#1f1f1f); }
  .about-brand-accent { color:var(--lf-color-accent,#f6bb38); }
  .about-copy-primary { color:var(--lf-color-text,#ffffff); }
  .about-copy-body { color:var(--lf-color-muted,#d4d4d8); }
  .about-copy-dim { color:var(--lf-color-muted,#a1a1aa); }
  /* About used a legacy text-4xl utility without a matching public sizing
     rule, leaving section H2 at 16–18px. Use the shared Academy section
     hierarchy, without changing body copy, imagery or page layout. */
  .source-about h2 {
    font-family:'Fira Sans Extra Condensed',sans-serif;
    font-size:clamp(22px,2vw,29px);
    font-weight:700;
    line-height:1.2;
    text-wrap:balance;
    overflow-wrap:anywhere;
  }
</style>
