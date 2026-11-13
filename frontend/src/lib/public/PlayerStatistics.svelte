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
  .player-statistics{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,160px),1fr));gap:1px;background:#ffffff18;border:1px solid #ffffff18;margin:0}
  .player-statistics div{display:flex;flex-direction:column-reverse;gap:10px;background:#121212;padding:24px 20px;min-width:0}
  dt{font-size:13px;line-height:1.45;color:#d5d5d5;letter-spacing:.025em;overflow-wrap:anywhere}dd{font-family:'Fira Sans',sans-serif;font-size:clamp(28px,4vw,42px);line-height:1.15;font-weight:800;margin:0;color:#f5a623}
  .statistics-note{color:#aaa;font-size:14px;line-height:1.55;margin-top:14px}
  @media(max-width:480px){.player-statistics{grid-template-columns:repeat(2,minmax(0,1fr))}.player-statistics div{padding:18px 14px}}
</style>
