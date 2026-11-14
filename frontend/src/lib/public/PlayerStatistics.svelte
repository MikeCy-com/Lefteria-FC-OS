<script lang="ts">
  import type { SectionStatistics } from './sections-types';
  let { statistics }: { statistics: SectionStatistics | null | undefined } = $props();
  const format = new Intl.NumberFormat('el-CY', { maximumFractionDigits: 2 });
  function average(value: string | number | null | undefined, appearances: string | number | null | undefined) {
    if (value == null || appearances == null || value === '' || appearances === '') return null;
    const total = Number(value);
    const matches = Number(appearances);
    return Number.isFinite(total) && Number.isFinite(matches) && matches > 0 ? format.format(total / matches) : null;
  }
  const items = $derived([
    ['ΣΥΜΜΕΤΟΧΕΣ', statistics?.appearances], ['ΓΚΟΛ', statistics?.goals], ['ΑΣΙΣΤ', statistics?.assists],
    ['Γκολ ανά αγώνα', average(statistics?.goals, statistics?.appearances)],
    ['Ασίστ ανά αγώνα', average(statistics?.assists, statistics?.appearances)],
    ['ΛΕΠΤΑ', statistics?.minutes_played], ['ΑΝΕΠΑΦΗ ΕΣΤΙΑ', statistics?.clean_sheets],
    ['ΚΙΤΡΙΝΕΣ ΚΑΡΤΕΣ', statistics?.yellow_cards], ['ΚΟΚΚΙΝΕΣ ΚΑΡΤΕΣ', statistics?.red_cards]
  ]);
</script>
<dl class="player-statistics" aria-label="Καταγεγραμμένα στατιστικά">{#each items as [label, value]}<div><dt>{label}</dt><dd>{value ?? '—'}</dd></div>{/each}</dl>
<p class="statistics-note">Η παύλα σημαίνει ότι δεν υπάρχει καταγεγραμμένο στοιχείο.</p>
