<script lang="ts">
  import type { SectionStatistics } from './sections-types';
  let { statistics }: { statistics: SectionStatistics | null | undefined } = $props();
  const format = new Intl.NumberFormat('el-CY', { maximumFractionDigits: 2 });
  function average(value: number | null | undefined, appearances: number | null | undefined) {
    return value != null && appearances != null && appearances > 0 ? format.format(value / appearances) : null;
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
