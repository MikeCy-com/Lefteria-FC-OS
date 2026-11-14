<script lang="ts">
  import type { SectionStatistics } from './sections-types';
  let { statistics }: { statistics: SectionStatistics | null | undefined } = $props();
  const items = $derived([
    ['ΣΥΜΜΕΤΟΧΕΣ', statistics?.appearances], ['ΓΚΟΛ', statistics?.goals], ['ΑΣΙΣΤ', statistics?.assists],
    ['ΛΕΠΤΑ', statistics?.minutes_played], ['ΑΝΕΠΑΦΗ ΕΣΤΙΑ', statistics?.clean_sheets],
    ['ΚΙΤΡΙΝΕΣ ΚΑΡΤΕΣ', statistics?.yellow_cards], ['ΚΟΚΚΙΝΕΣ ΚΑΡΤΕΣ', statistics?.red_cards]
  ]);
</script>
<dl class="player-statistics" aria-label="Καταγεγραμμένα στατιστικά">{#each items as [label, value]}<div><dt>{label}</dt><dd>{value ?? '—'}</dd></div>{/each}</dl>
<p class="statistics-note">Η παύλα σημαίνει ότι δεν υπάρχει καταγεγραμμένο στοιχείο.</p>
