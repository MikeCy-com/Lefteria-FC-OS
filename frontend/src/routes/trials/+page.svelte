<svelte:head>
  <title>ΔΟΚΙΜΑΣΤΙΚΑ ΠΡΩΤΗΣ ΟΜΑΔΑΣ | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content="Πληροφορίες για τις αιτήσεις δοκιμαστικών της πρώτης ομάδας Λευτεριά FC. Η διαθεσιμότητα και η αποστολή αίτησης δεν έχουν ακόμη ενεργοποιηθεί στο OS." />
</svelte:head>

<main class="trials-page" data-testid="trials-page">
  <div class="trials-shell">
    <a class="back-link" href="/" data-testid="trials-back-link">
      <span aria-hidden="true">←</span> Πίσω στην αρχική
    </a>

    <header class="trials-header">
      <div class="eyebrow">
        <svg aria-hidden="true" viewBox="0 0 24 24" width="28" height="28" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
          <path d="M8 21h8m-4-4v4M7 4h10v7a5 5 0 0 1-10 0V4Zm10 1h4v3a4 4 0 0 1-4 4M7 5H3v3a4 4 0 0 0 4 4"/>
        </svg>
        <span class="team-badge">ΠΡΩΤΗ ΟΜΑΔΑ</span>
      </div>
      <h1 data-testid="trials-headline">ΕΤΟΙΜΟΣ ΝΑ ΞΕΚΙΝΗΣΕΙΣ ΤΟ ΤΑΞΙΔΙ ΣΟΥ;</h1>
      <p class="subtitle">Δοκιμαστικά ποδοσφαίρου · Λευτεριά FC</p>
    </header>

    <section class="closed-card" aria-labelledby="trials-closed-heading" data-testid="trials-closed">
      <div class="closed-mark" aria-hidden="true">
        <svg viewBox="0 0 24 24" width="28" height="28" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>
        </svg>
      </div>
      <h2 id="trials-closed-heading">ΟΙ ΑΙΤΗΣΕΙΣ ΔΕΝ ΕΙΝΑΙ ΑΚΟΜΗ ΑΝΟΙΧΤΕΣ</h2>
      <p class="source-closed">Οι εγγραφές είναι κλειστές αυτή τη στιγμή.</p>
      <p class="safety-note">Δεν έχει επιβεβαιωθεί διαθέσιμη περίοδος δοκιμαστικών στο OS. Η υποβολή αίτησης δεν είναι ενεργή, επομένως δεν ζητάμε προσωπικά στοιχεία αθλητών και δεν εμφανίζουμε ψευδή επιβεβαίωση.</p>
      <div class="actions" aria-label="Επίσημες πληροφορίες">
        <a class="action-primary" href="/team">ΓΝΩΡΙΣΕ ΤΗΝ ΠΡΩΤΗ ΟΜΑΔΑ <span aria-hidden="true">↗</span></a>
        <a class="action-secondary" href="/contact">ΕΠΙΚΟΙΝΩΝΙΑ <span aria-hidden="true">→</span></a>
      </div>
    </section>
  </div>
</main>

<style>
  .trials-page {
    --trial-gold: #f5a623;
    min-height: max(85vh, 690px);
    padding: clamp(112px, 10vw, 176px) clamp(18px, 4.2vw, 72px) clamp(70px, 8vw, 144px);
    background: radial-gradient(ellipse 80% 46% at 50% 0%, rgb(245 166 35 / 6%), transparent 78%), #0a0a0a;
    color: #f8f8f8;
    font-family: 'Fira Sans', sans-serif;
  }
  .trials-shell { max-width: 1000px; margin: 0 auto; }
  .back-link { display: inline-flex; align-items: center; gap: 12px; color: #a5a5a5; font-size: 14px; text-decoration: none; transition: color 150ms ease; }
  .back-link:hover, .back-link:focus-visible { color: var(--trial-gold); }
  .back-link span { font-size: 22px; }
  .trials-header { margin-top: clamp(36px, 5vw, 78px); }
  .eyebrow { display: flex; gap: 14px; align-items: center; color: var(--trial-gold); }
  .team-badge { border: 1px solid rgb(245 166 35 / 56%); background: rgb(245 166 35 / 10%); padding: 7px 13px; font-weight: 800; font-size: 12px; line-height: 1.3; letter-spacing: .13em; }
  h1, h2 { font-family: 'Fira Sans Extra Condensed', 'Fira Sans', sans-serif; }
  h1 { margin: 21px 0 12px; max-width: 1000px; font-size: clamp(40px, 6.6vw, 96px); line-height: .98; letter-spacing: -.012em; font-weight: 900; text-wrap: balance; }
  .subtitle { color: #a4a4a4; margin: 0; font-size: clamp(15px, 1.5vw, 19px); }
  .closed-card {
    margin-top: clamp(30px, 4vw, 64px);
    border: 1px solid #34312a;
    border-top: 3px solid var(--trial-gold);
    background: linear-gradient(138deg, rgb(245 166 35 / 7%), transparent 54%), #141414;
    padding: clamp(26px, 4.5vw, 64px);
    box-shadow: 0 22px 70px rgb(0 0 0 / 32%);
  }
  .closed-mark { display: grid; place-items: center; width: 60px; height: 60px; margin-bottom: 23px; color: var(--trial-gold); background: rgb(245 166 35 / 9%); border: 1px solid rgb(245 166 35 / 26%); }
  h2 { margin: 0 0 12px; font-size: clamp(26px, 3vw, 43px); line-height: 1.08; font-weight: 800; }
  .source-closed { font-size: 17px; color: #d4d4d4; margin: 0 0 12px; }
  .safety-note { max-width: 760px; font-size: 14px; line-height: 1.7; color: #aaa; margin: 0; }
  .actions { display: flex; gap: 13px; flex-wrap: wrap; margin-top: 34px; }
  .actions a { display: inline-flex; align-items: center; justify-content: center; gap: 16px; padding: 15px 21px; min-height: 50px; text-decoration: none; font-size: 12px; font-weight: 800; letter-spacing: .045em; transition: background 150ms ease, color 150ms ease; }
  .action-primary { color: #090909; background: var(--trial-gold); }
  .action-primary:hover, .action-primary:focus-visible { background: #ffd077; }
  .action-secondary { color: #f5f5f5; border: 1px solid #5a554b; }
  .action-secondary:hover, .action-secondary:focus-visible { color: var(--trial-gold); border-color: var(--trial-gold); }
  :global(a:focus-visible) { outline: 2px solid #f5a623; outline-offset: 4px; }
  @media (max-width: 540px) {
    .trials-page { min-height: 72vh; }
    .actions { flex-direction: column; }
    .actions a { width: 100%; }
    .eyebrow { gap: 10px; }
    .closed-card { padding: 25px 20px; }
  }
  @media (prefers-reduced-motion: reduce) {
    .back-link, .actions a { transition: none; }
  }
</style>
