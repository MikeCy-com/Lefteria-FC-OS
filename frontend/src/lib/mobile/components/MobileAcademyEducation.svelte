<script lang="ts">
  import {
    ACADEMY_EDUCATION_ARTICLES, EDUCATION_STAGES, EDUCATION_TOPICS,
    searchAcademyEducation, type EducationAudience, type EducationStage, type EducationTopic
  } from '../academy-education.ts';
  import { recommendStarterEducation } from '../education-recommendations.ts';
  import './mobile-theme.css';

  let { viewerRole = 'parent' }: { viewerRole?: 'parent' | 'player' | 'fan' } = $props();
  let stage = $state<EducationStage>('10-12');
  // Fan interest is a UI preference only; an actual parent/player role is fixed.
  // Topic interest only. 'both' does not grant parent or player authorization.
  let fanInterest = $state<EducationAudience | 'both'>('parent');
  // Fans are always blocked from starter article bodies below, regardless of interest.
  const audience = $derived<EducationAudience>(viewerRole === 'fan'
    ? (fanInterest === 'player' ? 'player' : 'parent') : viewerRole);
  let expanded = $state<string | null>(null);
  let topic = $state<EducationTopic | 'all'>('all');
  let search = $state('');
  // Session-local preference only. No child identity, roster, or analytics data
  // are sent to the AI provider or stored as part of this starter experience.
  let completedArticleIds = $state<string[]>([]);
  const cards = $derived.by(() => {
    const matching = searchAcademyEducation(stage,audience,topic,search);
    if (viewerRole === 'fan') return [];
    const recommended = recommendStarterEducation({
      role: viewerRole,
      stage,
      preferredTopics: topic === 'all' ? [] : [topic],
      completedArticleIds,
      limit: 10
    });
    const order = new Map(recommended.map((item, index) => [item.id, index]));
    return [...matching].sort((left, right) =>
      (order.get(left.id) ?? 100) - (order.get(right.id) ?? 100));
  });
  const id = $props.id();
</script>

<section class="academy-learning" aria-labelledby={id+'-title'} data-testid="parent-education-hub" data-hub-role={viewerRole}>
  <header class="education-hero">
    <p class="education-kicker">ΛΕΥΤΕΡΙΑ FC · ΑΚΑΔΗΜΙΑ</p>
    <h1 id={id+'-title'}>HUB · Μαθαίνουμε μαζί</h1>
    <p>{viewerRole === 'player' ? 'Μικρά βήματα για να μαθαίνεις, να συνεργάζεσαι και να χαίρεσαι το παιχνίδι.' : 'Μικρές ιδέες για τη στήριξη των νεαρών αθλητών και την ψυχολογία κάθε ηλικίας.'}</p>
    <div class="education-hero-art" aria-hidden="true">
      <span class="education-art-ball">⚽</span><span class="education-art-heart">♥</span>
      <span class="education-art-star">✦</span>
    </div>
  </header>

  {#if viewerRole === 'fan'}
    <div class="education-editorial" data-testid="hub-fan-preview">
      <strong>Προεπισκόπηση HUB για φιλάθλους.</strong>
      Επίλεξε αν σε ενδιαφέρει υλικό για γονείς ή αθλητές.
      Η επιλογή ισχύει μόνο σε αυτή την οθόνη: δεν δημιουργεί συνδρομή,
      δεν αλλάζει τον ρόλο και δεν δίνει πρόσβαση σε δεδομένα παιδιών.
      Η εγγραφή θα απαιτεί ξεχωριστή επαληθευμένη υπηρεσία.
    </div>
  {/if}
  <div class="education-editorial" data-testid="education-editorial-status">
    Εκπαιδευτικό υλικό γνωριμίας του Lefteria FC OS. Δεν αποτελεί ακόμη επίσημη δημοσίευση
    ή έγκριση της Ακαδημίας. Δεν αντικαθιστά εξατομικευμένη υποστήριξη από επαγγελματία.
  </div>

  <section class="education-filters" aria-labelledby={id+'-age'}>
    <h2 id={id+'-age'}>Επίλεξε ηλικιακή ομάδα</h2>
    <p>Η επιλογή δεν συνδέεται με τα προσωπικά στοιχεία των παιδιών σου.</p>
    <div class="education-age-tabs" role="group" aria-label="Ηλικιακή ομάδα">
      {#each EDUCATION_STAGES as option (option.id)}
        <button type="button" aria-pressed={stage === option.id}
          class:chosen={stage === option.id} data-testid="education-stage"
          onclick={() => {stage = option.id; expanded = null;}}>
          {option.title}
        </button>
      {/each}
    </div>
    <p class="education-stage-hint" aria-live="polite" aria-atomic="true">
      {EDUCATION_STAGES.find(option => option.id === stage)?.hint}
    </p>
    {#if viewerRole === 'fan'}
      <div class="education-audiences" role="group" aria-label="Ενδιαφέρον HUB">
        <button type="button" aria-pressed={fanInterest === 'parent'}
          class:chosen={fanInterest === 'parent'}
          onclick={() => {fanInterest = 'parent'; expanded = null;}}>Για γονείς</button>
        <button type="button" aria-pressed={fanInterest === 'player'}
          class:chosen={fanInterest === 'player'}
          onclick={() => {fanInterest = 'player'; expanded = null;}}>Για αθλητές</button>
        <button type="button" aria-pressed={fanInterest === 'both'}
          class:chosen={fanInterest === 'both'}
          onclick={() => {fanInterest = 'both'; expanded = null;}}>Και τα δύο</button>
      </div>
    {:else}
      <p class="education-role-scope" data-testid="education-role-scope">
        {viewerRole === 'parent'
          ? 'Εκπαιδευτικό υλικό για γονείς και κηδεμόνες'
          : 'Εκπαιδευτικό υλικό για νεαρούς αθλητές'}
      </p>
    {/if}
    <div class="education-topics" role="group" aria-label="Θέμα">
      <button type="button" aria-pressed={topic === 'all'} class:chosen={topic === 'all'}
        onclick={() => {topic = 'all'; expanded = null;}}>Όλα τα θέματα</button>
      {#each EDUCATION_TOPICS as option (option.id)}
        <button type="button" aria-pressed={topic === option.id}
          class:chosen={topic === option.id} onclick={() => {topic = option.id; expanded = null;}}>
          {option.title}
        </button>
      {/each}
    </div>
  </section>

  <div class="education-search">
    <label for={id+'-search'}>Αναζήτηση στο HUB</label>
    <input id={id+'-search'} type="search" maxlength="120" autocomplete="off"
      placeholder="Αναζήτησε άρθρα και συμβουλές" bind:value={search}
      oninput={() => {expanded = null;}} data-testid="hub-content-search" />
    {#if search}
      <button type="button" onclick={() => {search = ''; expanded = null;}}
        aria-label="Καθαρισμός αναζήτησης">Καθαρισμός</button>
    {/if}
  </div>
  {#if viewerRole === 'fan'}
    <section class="education-subscription-gate" aria-labelledby={id+'-subscription'} data-testid="hub-subscription-required">
      <h2 id={id+'-subscription'}>Συνδρομή HUB</h2>
      <p>Το εκπαιδευτικό περιεχόμενο για φιλάθλους παρέχεται με ξεχωριστή συνδρομή HUB. Μπορείς να επιλέξεις ενδιαφέρον για γονείς, αθλητές ή και τα δύο, χωρίς να αποκτάς αντίστοιχο ρόλο.</p>
      <p role="status">Επιλεγμένο ενδιαφέρον: {fanInterest==='both'?'Γονείς και αθλητές':fanInterest==='parent'?'Γονείς':'Αθλητές'} (μόνο σε αυτή την οθόνη).</p>
      <p>Η εγγραφή, η τιμολόγηση και η επαλήθευση ενεργής συνδρομής δεν είναι ακόμη διαθέσιμες. Δεν πραγματοποιείται χρέωση.</p>
      <p class="education-result-count" role="status">Το περιεχόμενο παραμένει κλειδωμένο έως την επιβεβαίωση ενεργής συνδρομής.</p>
    </section>
  {:else}
  <section class="education-articles" aria-labelledby={id+'-materials'} data-testid="education-materials">
    <h2 id={id+'-materials'}>Ιδέες για συζήτηση</h2>
    {#if cards.length === 0}
      <p class="education-empty" role="status">Δεν υπάρχουν άρθρα για αυτόν τον συνδυασμό. Δοκίμασε άλλο θέμα.</p>
    {/if}
    <p class="education-result-count" role="status" aria-live="polite" aria-atomic="true">
      {cards.length} {cards.length === 1 ? 'άρθρο' : 'άρθρα'} για την επιλεγμένη ομάδα
    </p>
    <div class="education-card-grid">
      {#each cards as article (article.id)}
        <article class="education-card" data-testid="education-article">
          <div class="education-card-heading">
            <span>{article.audience === 'parent' ? 'ΓΙΑ ΓΟΝΕΙΣ' : 'ΓΙΑ ΑΘΛΗΤΕΣ'}</span>
            <span>{EDUCATION_TOPICS.find(topic => topic.id === article.topic)?.title}</span>
          </div>
          <h3>{article.title}</h3>
          <p>{article.summary}</p>
          <button class="education-expand" type="button" aria-expanded={expanded === article.id}
            aria-controls={id+'-'+article.id}
            onclick={() => { expanded = expanded === article.id ? null : article.id; }}>
            {expanded === article.id ? 'Κλείσιμο' : 'Διάβασε περισσότερα'}
            <span aria-hidden="true">{expanded === article.id ? '−' : '+'}</span>
          </button>
          {#if expanded === article.id}
            <div class="education-details" id={id+'-'+article.id}>
              <h4>Τι μπορώ να δοκιμάσω</h4>
              <ul>
                {#each article.points as point (point)}<li>{point}</li>{/each}
              </ul>
              <button class="education-complete" type="button"
                aria-pressed={completedArticleIds.includes(article.id)}
                onclick={() => {
                  completedArticleIds = completedArticleIds.includes(article.id)
                    ? completedArticleIds.filter(item => item !== article.id)
                    : [...completedArticleIds, article.id];
                }}>
                {completedArticleIds.includes(article.id) ? '✓ Ολοκληρώθηκε · Αναίρεση' : 'Σημείωσε ως ολοκληρωμένο'}
              </button>
              <div class="education-conversation">
                <strong>Μια ερώτηση για συζήτηση</strong>
                <p>{article.discussion}</p>
              </div>
            </div>
          {/if}
        </article>
      {/each}
    </div>
  </section>
  {/if}
  <aside class="education-boundary">
    <h2>Έχεις κάποια ανησυχία;</h2>
    <p>Για δυσκολίες που επιμένουν, μίλησε με έναν έμπιστο ενήλικα,
      τον προπονητή ή έναν κατάλληλο επαγγελματία. Η προσωπική εμπειρία του παιδιού έχει σημασία.</p>
    <p>Οι εικόνες του μελλοντικού Κέντρου Εκπαίδευσης και το υλικό που θα δημοσιεύει
      η Ακαδημία θα προστεθούν μετά από έλεγχο περιεχομένου και δικαιωμάτων.</p>
  </aside>
</section>

<style>
  .academy-learning{width:min(100%,930px);margin:0 auto;padding-bottom:35px;color:#f2f2f2}
  .education-hero{position:relative;overflow:hidden;padding:29px 30px 40px;border-radius:15px;border:1px solid #665022;background:linear-gradient(125deg,#35230b,#181b1b 70%);margin-bottom:15px}
  .education-kicker{margin:0 0 10px;color:#f5b329;font-size:11px;font-weight:700;letter-spacing:.12em}
  .education-hero h1{font-family:'Fira Sans Extra Condensed',sans-serif;font-size:clamp(36px,7vw,54px);margin:0 0 12px}
  .education-hero>p:not(.education-kicker){max-width:530px;font-size:15px;line-height:1.7;color:#e0dcce;margin:0;position:relative;z-index:1}
  .education-hero-art{position:absolute;right:20px;top:4px;display:flex;align-items:center;gap:10px;opacity:.22;font-size:65px;transform:rotate(-14deg)}
  .education-art-heart{font-size:30px;color:#f8ba36}.education-art-star{font-size:35px}
  .education-editorial{padding:13px 16px;border:1px solid #45413a;border-radius:9px;color:#b9bbba;background:#171a1b;font-size:12px;line-height:1.65;margin-bottom:26px}
  .education-filters h2,.education-articles h2,.education-boundary h2{font-size:22px;margin:0 0 9px}
  .education-filters>p{color:#b6bbbd;font-size:13px;line-height:1.5}
  .education-age-tabs,.education-audiences,.education-topics{display:flex;flex-wrap:wrap;gap:8px;margin:14px 0}
  .education-age-tabs button,.education-audiences button,.education-topics button{min-height:44px;padding:10px 15px;border:1px solid #383d3d;border-radius:8px;background:#202323;color:#d2d6d5;cursor:pointer;font-size:13px}
  .education-age-tabs button.chosen,.education-audiences button.chosen,.education-topics button.chosen{border-color:#f0ad29;color:#f9c64d;background:#302514}
  .education-role-scope{color:#f8cb74;font-size:13px;font-weight:700;margin:14px 0}\n  .education-stage-hint{color:#f8cb74!important;font-size:13px!important}
  .education-search{display:flex;flex-wrap:wrap;gap:9px;align-items:center;margin:20px 0}\n  .education-search label{flex:0 0 100%;font-size:13px;font-weight:700;color:#e8e8e8}\n  .education-search input{flex:1;min-width:180px;min-height:44px;background:#181a1b;color:#fff;border:1px solid #5b5e60;border-radius:8px;padding:10px 12px;font:inherit}\n  .education-search button{min-height:44px;padding:10px 14px;border:1px solid #695329;border-radius:8px;background:#2c2519;color:#ffd47a;cursor:pointer}\n  .education-subscription-gate{margin:24px 0;padding:22px;border:1px solid #7b6237;border-radius:12px;background:linear-gradient(145deg,#302413,#171a1b);color:#eee}\n  .education-subscription-gate h2{font-size:23px;margin:0 0 10px}\n  .education-subscription-gate p{font-size:13px;line-height:1.7}\n  .education-articles{margin:28px 0}
  .education-result-count{color:#c8ceca;font-size:13px;line-height:1.5;margin:0 0 10px}
  .education-empty{padding:12px 15px;border:1px solid #48433b;border-radius:8px;color:#e1d5ba;font-size:13px}
  .education-card-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:15px;margin-top:15px}
  .education-card{min-width:0;padding:20px;border:1px solid #3a3b37;border-radius:12px;background:linear-gradient(145deg,#202323,#151718)}
  .education-card-heading{display:flex;flex-wrap:wrap;gap:8px;font-size:10px;color:#f8b844;margin-bottom:14px}
  .education-card-heading>span{padding:5px 9px;border:1px solid #615031;border-radius:15px}
  .education-card h3{font-size:21px;margin:0 0 12px}
  .education-card>p{font-size:13px;line-height:1.75;color:#c9c9c7}
  .education-expand{display:flex;align-items:center;justify-content:space-between;gap:15px;min-height:44px;width:100%;padding:10px 0;margin-top:13px;border:0;border-top:1px solid #41403a;background:transparent;color:#ffca45;font-weight:700;text-align:left;cursor:pointer}
  .education-expand span{font-size:21px}
  .education-details{border-top:1px solid #41403a;padding:12px 0 0;font-size:13px;line-height:1.7}
  .education-details h4{margin:0 0 8px}
  .education-details ul{padding-left:22px;margin:0 0 18px;color:#d8d9d6}
  .education-details li{margin-bottom:8px}
  .education-complete{display:block;min-height:44px;margin:12px 0;padding:10px 14px;border:1px solid #5a6b4b;border-radius:8px;background:#1c2b1d;color:#e1efc5;cursor:pointer}
  .education-complete[aria-pressed='true']{border-color:#9bbb78;background:#273e2c}
  .education-conversation{border-left:3px solid #efae31;padding:10px 13px;background:#2b261d}
  .education-conversation p{margin:5px 0 0}
  .education-boundary{border:1px solid #48433b;border-radius:12px;background:#1a1d1d;padding:22px}
  .education-boundary p{font-size:13px;line-height:1.7;color:#c7c7c6}
  button:focus-visible{outline:2px solid #f5bd43;outline-offset:3px}
  @media(max-width:650px){.education-card-grid{grid-template-columns:1fr}.education-hero{padding:24px}.education-hero-art{font-size:42px}}
</style>
