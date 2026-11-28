<script lang="ts">
  import PublicShell from '../../lib/public/PublicShell.svelte';
  import PublicImage from '../../lib/public/PublicImage.svelte';
  import { personPublicLink } from '../../lib/public/public-urls';
  import { potmMonthLabel } from '../../lib/public/potm';
  import type { PotmPageData } from './+page.server';

  let { data }: { data: PotmPageData } = $props();
  const result = $derived(data.potm);
  const month = $derived(potmMonthLabel(result.monthKey));
  const snapshot = $derived(result.snapshotAt ? new Intl.DateTimeFormat('el-GR', {
    dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Nicosia'
  }).format(new Date(result.snapshotAt)) : null);
</script>

<svelte:head>
  <title>ΠΑΙΚΤΗΣ ΤΟΥ ΜΗΝΑ | ΛΕΥΤΕΡΙΑ FC</title>
  <meta name="description" content="Δημοσιευμένοι ποδοσφαιριστές της πρώτης ομάδας και διαθέσιμα αποτελέσματα του παίκτη του μήνα." />
</svelte:head>

<PublicShell site={data.site} branding={data.branding} organization="lefteria_fc" navigationGroups={data.navigationGroups}>
  <div class="potm-page" data-testid="potm-page">
    <section class="potm-heading" aria-labelledby="potm-title">
      <p class="public-eyebrow">ΛΕΥΤΕΡΙΑ FC · ΠΡΩΤΗ ΟΜΑΔΑ</p>
      <h1 id="potm-title">ΠΑΙΚΤΗΣ <span>ΤΟΥ ΜΗΝΑ</span></h1>
      {#if month}<p class="month" data-testid="potm-month">{month}</p>{/if}
      <p>Γνώρισε τους ποδοσφαιριστές της πρώτης ομάδας και δες τα διαθέσιμα δημοσιευμένα αποτελέσματα.</p>
      <div class="links">
        <a class="public-button" href="/team?tab=roster">ΡΟΣΤΕΡ ↗</a>
        <a class="public-button" href="/app?tab=potm">ΨΗΦΟΦΟΡΙΑ ΣΤΗΝ ΕΦΑΡΜΟΓΗ ↗</a>
      </div>
    </section>

    <section class="potm-results" aria-labelledby="potm-results-title">
      <div class="vote-notice" role="status" data-testid="potm-voting-disabled">
        <strong>ΕΝΗΜΕΡΩΣΗ ΨΗΦΟΦΟΡΙΑΣ</strong>
        <p>Η ψηφοφορία γίνεται μόνο στην εφαρμογή με επαληθευμένο λογαριασμό φιλάθλου, όταν η περίοδος είναι ανοικτή. Δεν υποβάλλονται ούτε αλλάζουν ψήφοι από αυτή τη δημόσια σελίδα.</p>
      </div>
      <h2 id="potm-results-title">{result.status === 'published' ? 'ΔΗΜΟΣΙΕΥΜΕΝΑ ΑΠΟΤΕΛΕΣΜΑΤΑ' : 'ΠΟΔΟΣΦΑΙΡΙΣΤΕΣ ΠΡΩΤΗΣ ΟΜΑΔΑΣ'}</h2>
      {#if result.status === 'published'}
        <p class="tally" data-testid="potm-total-votes"><strong>{result.totalVotes}</strong> καταγεγραμμένες ψήφοι στο δημοσιευμένο στιγμιότυπο</p>
        <p class="snapshot">Στιγμιότυπο: {snapshot} · Ώρα Κύπρου. Δεν πρόκειται για ζωντανή καταμέτρηση ή τελικό νικητή.</p>
      {:else if result.status === 'not-published'}
        <p class="state" role="status" data-testid="potm-no-published-results">Δεν υπάρχουν δημοσιευμένα αποτελέσματα για τον μήνα αυτό.</p>
      {:else}
        <p class="state" role="status" data-testid="potm-results-unavailable">Τα αποτελέσματα δεν είναι προσωρινά διαθέσιμα ή δεν έχουν πλήρως επαληθευτεί.</p>
      {/if}

      {#if result.candidates.length}
        <div class="candidate-grid" data-testid="potm-candidates">
          {#each result.candidates as player (player.id)}
            <article class="candidate" data-testid="potm-candidate">
              <div class="portrait"><PublicImage src={player.imageUrl} alt={player.name} /></div>
              <div class="candidate-details">
                <p class="number">#{player.number}{player.position ? ' · ' + player.position : ''}</p>
                <h3><a href={personPublicLink('player', player)}>{player.name}</a></h3>
                {#if result.status === 'published' && player.votes !== null}
                  <p class="candidate-votes" data-testid="potm-candidate-votes">{player.votes} ψήφοι</p>
                {/if}
              </div>
            </article>
          {/each}
        </div>
      {:else}
        <p class="state" data-testid="potm-roster-empty">Δεν υπάρχουν διαθέσιμοι δημοσιευμένοι ποδοσφαιριστές.</p>
      {/if}
    </section>
  </div>
</PublicShell>

<style>
  .potm-page{width:min(100%,var(--lf-layout-page-max-width,1280px));margin:auto;padding:clamp(16px,3vw,40px);box-sizing:border-box}
  .potm-heading{padding:clamp(20px,4vw,48px);background:var(--lf-color-surface,#151719);border:1px solid var(--lf-color-border,#3b3830);border-radius:var(--lf-layout-card-radius,10px)}
  .potm-heading h1{margin:8px 0 12px;font-size:clamp(30px,5vw,52px);line-height:1.08;overflow-wrap:anywhere}
  .potm-heading h1 span,.month,.number,.tally strong{color:var(--lf-color-accent,#f6bb38)}
  .potm-heading p,.snapshot,.state{line-height:1.55}
  .month{font-weight:700;text-transform:capitalize}
  .links{display:flex;flex-wrap:wrap;gap:12px;margin-top:20px}
  .potm-results{margin-top:24px}
  .vote-notice{border:1px solid var(--lf-color-accent,#f6bb38);padding:16px;border-radius:8px;margin-bottom:24px;background:var(--lf-color-surface,#151719)}
  .vote-notice strong{color:var(--lf-color-accent,#f6bb38)}
  .vote-notice p{margin:6px 0 0;line-height:1.5}
  .potm-results h2{font-size:clamp(23px,3vw,34px);line-height:1.2}
  .snapshot,.state{color:var(--lf-color-muted,#b9bbbc);font-size:14px}
  .candidate-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,220px),1fr));gap:var(--lf-layout-card-gap,14px);margin-top:20px}
  .candidate{min-width:0;display:grid;grid-template-columns:74px minmax(0,1fr);align-items:center;gap:14px;padding:14px;background:var(--lf-color-surface,#151719);border:1px solid var(--lf-color-border,#343434);border-radius:var(--lf-layout-card-radius,8px)}
  .portrait{width:74px;aspect-ratio:1;overflow:hidden;border-radius:6px}
  .portrait :global(img){width:100%;height:100%;object-fit:cover}
  .candidate-details h3{margin:5px 0;font-size:19px;line-height:1.15;overflow-wrap:anywhere}
  .candidate-details a{color:var(--lf-color-text,#ffffff);text-decoration:none}
  .candidate-details a:focus-visible,.candidate-details a:hover{color:var(--lf-color-accent,#f6bb38)}
  .number,.candidate-votes{margin:0;font-size:13px;font-weight:600;overflow-wrap:anywhere}
</style>
