<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import PublicIcon from '../../lib/public/PublicIcon.svelte';
  import type { PublicIconName } from '../../lib/public/icon-types';
  import type { PublicPageData } from '../../lib/public/types';
  let { data }: { data: PublicPageData } = $props();
  const values: {icon: PublicIconName;title:string;desc:string}[] = [{"title": "Αριστεία", "desc": "Επιδιώκουμε την αριστεία σε όλα όσα κάνουμε, εντός και εκτός γηπέδου.", "icon": "trophy"}, {"title": "Πάθος", "desc": "Το ποδόσφαιρο είναι περισσότερο από ένα παιχνίδι - είναι το πάθος και ο τρόπος ζωής μας.", "icon": "heart"}, {"title": "Φιλοδοξία", "desc": "Θέτουμε υψηλούς στόχους και εργαζόμαστε ακούραστα για να τους επιτύχουμε.", "icon": "target"}];
</script>
<svelte:head><title>Σχετικά με τη ΛΕΥΤΕΡΙΑ FC</title><meta name="description" content="Η ιστορία, οι αξίες και η έδρα της Λευτεριάς FC." /></svelte:head>
<PublicShell site={data.site} organization={data.organization} navigationGroups={data.navigationGroups} branding={data.branding}>
  <div class="source-about pt-24 min-h-screen" data-testid="about-page">

    <section class="py-10 md:py-20 px-4 md:px-6 bg-[#0a0a0a]">
      <div class="max-w-7xl mx-auto">
        <span class="badge badge-secondary mb-4">Η Ιστορια μας</span>
        <h1 class="font-['Bebas_Neue'] text-4xl md:text-5xl text-white mb-6">
          Σχετικα με την <span class="text-[#F5A623]">ΛΕΥΤΕΡΙΑ FC</span>
        </h1>
        <p class="text-xl text-zinc-300 max-w-3xl">
          Ιδρύθηκε το 2024, η ΛΕΥΤΕΡΙΑ FC ενσαρκώνει το πνεύμα του πάθους, της αριστείας
          και της κοινότητας στο κυπριακό ποδόσφαιρο.
        </p>
      </div>
    </section>


    <section class="py-10 md:py-20 px-4 md:px-6">
      <div class="max-w-7xl mx-auto grid lg:grid-cols-2 gap-6 lg:gap-12 items-center">
        <div>
          <h2 class="font-['Bebas_Neue'] text-4xl text-white mb-6 section-heading">Η Ιστορια μας</h2>
          <div class="space-y-4 text-zinc-300">
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


    <section class="py-10 md:py-20 px-4 md:px-6 bg-[#0a0a0a]">
      <div class="max-w-7xl mx-auto">
        <h2 class="font-['Bebas_Neue'] text-4xl text-white mb-12 text-center">Οι Αξιες μας</h2>
        <div class="grid md:grid-cols-3 gap-8">
          {#each values as value, i}
<div class="card p-8 text-center">
              <PublicIcon name={value.icon} size={48} />
              <h3 class="font-['Bebas_Neue'] text-2xl text-white mb-3">{value.title}</h3>
              <p class="text-zinc-400">{value.desc}</p>
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
            <h2 class="font-['Bebas_Neue'] text-4xl text-white mb-6 section-heading">Γηπεδο Αετου</h2>
            <div class="space-y-4 text-zinc-300">
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
          <div class="aspect-video bg-[#1F1F1F] rounded-none overflow-hidden">
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
.source-about section{padding:80px 24px}.source-about section:first-child{padding-top:56px;background:#0a0a0a}.source-about .max-w-7xl{max-width:1400px;margin:auto}.source-about h1{margin:0 0 24px}.source-about h2{margin:0 0 24px}.source-about h3{margin:0 0 12px}.source-about .grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:48px;align-items:center}.source-about [class*="md:grid-cols-3"]{grid-template-columns:repeat(3,minmax(0,1fr));gap:32px}.source-about .card{padding:32px;text-align:center}.source-about .card p{color:#a1a1aa;line-height:1.5}.source-about :global(svg){color:#f5a623}.source-about .card :global(svg){margin:auto auto 16px}.source-about [class*="text-[#F5A623]"]{color:#f5a623}.source-about p{color:#d4d4d8;font-size:16px;line-height:1.5;margin:0 0 16px}.source-about section:first-child p{font-size:20px;max-width:768px}.source-about .text-center{text-align:center}.source-about ul{padding:0;list-style:none}.source-about li{display:flex;gap:12px;align-items:center;margin-bottom:8px;color:#d4d4d8}.source-about .badge{display:inline-block;margin-bottom:16px}.source-about :global(.max-w-md){max-width:448px;margin:auto}.source-about :global(img){width:100%;object-fit:contain}.source-about .aspect-video{aspect-ratio:16/9;background:#1f1f1f;overflow:hidden}.source-about .aspect-video :global(img){height:100%;object-fit:cover}@media(max-width:1023px){.source-about .grid{grid-template-columns:1fr;gap:24px}.source-about [class*="md:grid-cols-3"]{grid-template-columns:repeat(3,minmax(0,1fr))}}@media(max-width:767px){.source-about section{padding:40px 16px}.source-about section:first-child{padding-top:35px}.source-about [class*="md:grid-cols-3"]{grid-template-columns:1fr}}
</style>
