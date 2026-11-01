<script lang="ts">
  import PublicShell from '../../../lib/public/PublicShell.svelte';
  import PublicIcon from '../../../lib/public/PublicIcon.svelte';
  import type { PublicIconName } from '../../../lib/public/icon-types';
  import type { PublicPageData } from '../../../lib/public/types';
  let { data }: { data: PublicPageData } = $props();
  const values: {icon: PublicIconName;title:string;desc:string}[] = [{"title": "ΠΑΘΟΣ", "desc": "Καλλιεργουμε την αγαπη για το ποδοσφαιρο. Καθε προπονηση ειναι μια ευκαιρια να ζησεις τη χαρα του αθληματος.", "icon": "heart"}, {"title": "ΠΕΙΘΑΡΧΙΑ", "desc": "Μαθαινουμε στους νεους αθλητες τη σημασια της δεσμευσης, της συνεπειας και του σεβασμου.", "icon": "shield"}, {"title": "ΟΜΑΔΙΚΟΤΗΤΑ", "desc": "Το ποδοσφαιρο ειναι ομαδικο αθλημα. Χτιζουμε δεσμους, εμπιστοσυνη και συνεργασια.", "icon": "users"}, {"title": "ΑΝΑΠΤΥΞΗ", "desc": "Καθε παικτης εχει ατομικο πλανο αναπτυξης. Εστιαζουμε στη βελτιωση, οχι μονο στο αποτελεσμα.", "icon": "target"}, {"title": "ΑΡΙΣΤΕΙΑ", "desc": "Στοχευουμε στο υψηλοτερο επιπεδο σε ο,τι κανουμε. Απο την προπονηση μεχρι τη συμπεριφορα.", "icon": "star"}, {"title": "ΚΑΙΝΟΤΟΜΙΑ", "desc": "Χρησιμοποιουμε συγχρονες μεθοδους προπονησης και τεχνολογια για την αναπτυξη των αθλητων.", "icon": "zap"}];
  const approaches = [{"title": "Εξατομικευμενη Αναπτυξη", "desc": "Καθε παικτης αξιολογειται ξεχωριστα. Δημιουργουμε ατομικα πλανα αναπτυξης με βαση τις αναγκες και τα δυνατα σημεια του καθε αθλητη."}, {"title": "Δομημενο Προγραμμα", "desc": "Ακολουθουμε ενα επιστημονικα τεκμηριωμενο προγραμμα προπονησης που εξελισσεται αναλογα με την ηλικια και το επιπεδο καθε ομαδας."}, {"title": "Ασφαλες Περιβαλλον", "desc": "Η ασφαλεια και η ευημερια των αθλητων μας ειναι η πρωτη μας προτεραιοτητα. Καθε παιδι πρεπει να νιωθει ασφαλες."}, {"title": "Συνεργασια με Γονεις", "desc": "Οι γονεις ειναι αναποσπαστο μερος του ταξιδιου. Παρεχουμε τακτικη ενημερωση, αξιολογησεις και ανοιχτη επικοινωνια."}];
</script>
<svelte:head><title>Η Φιλοσοφία μας | Ακαδημία Λευτεριά FC</title><meta name="description" content="Οι αξίες και η προπονητική προσέγγιση της Ακαδημίας Λευτεριά FC." /></svelte:head>
<PublicShell site={data.site} organization="lefteria_fc_academy" navigationGroups={data.navigationGroups} branding={data.branding}>
    <div class="source-philosophy min-h-screen bg-black" data-testid="philosophy-page">

      <section class="pt-32 pb-16 px-6">
        <div class="max-w-7xl mx-auto">
          <span class="badge badge-secondary mb-4">Ακαδημια Λευτεριά FC</span>
          <h1 class="font-['Bebas_Neue'] text-5xl md:text-7xl text-white tracking-wide">
            Η ΦΙΛΟΣΟΦΙΑ <span class="text-[#F5A623]">ΜΑΣ</span>
          </h1>
          <p class="text-zinc-400 text-lg max-w-2xl mt-6 leading-relaxed">
            Πιστευουμε οτι καθε νεος αθλητης αξιζει ενα περιβαλλον που τον αναπτυσσει ολοπλευρα — τεχνικα, τακτικα, σωματικα και ψυχολογικα.
          </p>
        </div>
      </section>

      <div class="max-w-7xl mx-auto px-6"><div class="h-px bg-[#262626]" ></div></div>


      <section class="py-16 px-6">
        <div class="max-w-7xl mx-auto">
          <h2 class="font-['Bebas_Neue'] text-3xl text-white mb-2">ΟΙ ΑΞΙΕΣ <span class="text-[#F5A623]">ΜΑΣ</span></h2>
          <div class="w-12 h-1 bg-[#F5A623] mb-10" ></div>
          <div class="grid md:grid-cols-2 lg:grid-cols-3 gap-5">
            {#each values as val, i}
<div class="bg-[#111] border border-[#262626] rounded-lg p-6" data-testid={`value-${i}`}>
                <PublicIcon name={val.icon} size={24} />
                <h3 class="font-['Bebas_Neue'] text-xl text-white mb-2">{val.title}</h3>
                <p class="text-zinc-500 text-sm leading-relaxed">{val.desc}</p>
              </div>
{/each}
          </div>
        </div>
      </section>

      <div class="max-w-7xl mx-auto px-6"><div class="h-px bg-[#262626]" ></div></div>


      <section class="py-16 px-6" data-testid="approach-section">
        <div class="max-w-7xl mx-auto">
          <h2 class="font-['Bebas_Neue'] text-3xl text-white mb-2">Η ΠΡΟΣΕΓΓΙΣΗ <span class="text-[#F5A623]">ΜΑΣ</span></h2>
          <div class="w-12 h-1 bg-[#F5A623] mb-10" ></div>
          <div class="grid md:grid-cols-2 gap-5">
            {#each approaches as item, i}
<div class="bg-[#111] border border-[#262626] rounded-lg p-6 flex gap-5">
                <div class="w-10 h-10 rounded bg-[#F5A623]/10 flex items-center justify-center flex-shrink-0">
                  <span class="text-[#F5A623] font-['Bebas_Neue'] text-xl">{i + 1}</span>
                </div>
                <div>
                  <h4 class="font-['Bebas_Neue'] text-lg text-white mb-2">{item.title}</h4>
                  <p class="text-zinc-500 text-sm leading-relaxed">{item.desc}</p>
                </div>
              </div>
{/each}
          </div>
        </div>
      </section>
    </div>
</PublicShell>
<style>
.source-philosophy{background:black}.source-philosophy section{padding:64px 24px}.source-philosophy section:first-child{padding-top:56px}.source-philosophy .max-w-7xl{max-width:1400px;margin:auto}.source-philosophy h1{margin:0}.source-philosophy section:first-child p{max-width:672px;font-size:18px;color:#a1a1aa;line-height:1.625;margin-top:24px}.source-philosophy [class*="text-[#F5A623]"],.source-philosophy :global(svg){color:#f5a623}.source-philosophy .grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:20px}.source-philosophy [class*="lg:grid-cols-3"]{grid-template-columns:repeat(3,minmax(0,1fr))}.source-philosophy .grid>div{padding:24px;border:1px solid #262626;background:#111}.source-philosophy h2,.source-philosophy h3,.source-philosophy h4{margin:0 0 8px}.source-philosophy .grid p{color:#71717a;font-size:14px;line-height:1.625;margin:0}.source-philosophy .grid :global(svg){margin-bottom:16px}.source-philosophy .h-px{height:1px;background:#262626}.source-philosophy .w-12{width:48px;height:4px;background:#f5a623;margin-bottom:40px}.source-philosophy [data-testid="approach-section"] .grid>div{display:flex;gap:20px}.source-philosophy .w-10{width:40px;height:40px;flex-shrink:0;background:#f5a6231a;display:grid;place-items:center;font:20px 'Fira Sans Extra Condensed',sans-serif;color:#f5a623}@media(max-width:1023px){.source-philosophy [class*="lg:grid-cols-3"]{grid-template-columns:repeat(2,minmax(0,1fr))}}@media(max-width:767px){.source-philosophy section:first-child{padding-top:35px}.source-philosophy .grid,.source-philosophy [class*="lg:grid-cols-3"]{grid-template-columns:1fr}}
</style>
