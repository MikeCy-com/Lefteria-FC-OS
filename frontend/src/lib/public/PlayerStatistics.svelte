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
<style>
  .player-statistics{display:grid;grid-template-columns:repeat(auto-fit,minmax(130px,1fr));gap:1px;background:#ffffff18;border:1px solid #ffffff18;margin:0}
  .player-statistics div{display:flex;flex-direction:column-reverse;gap:9px;background:#121212;padding:25px 20px}
  dt{font-size:11px;color:#c7c7c7;letter-spacing:.06em}dd{font-family:'Fira Sans',sans-serif;font-size:42px;line-height:1;font-weight:800;margin:0;color:#f5a623}
  .statistics-note{color:#aaa;font-size:12px;margin-top:13px}
</style>
