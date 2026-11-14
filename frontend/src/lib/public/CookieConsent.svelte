<script lang="ts">
  import { onMount } from 'svelte';
  // Original components/CookieConsent.jsx: same key, text and choices; the choice stays in this browser.
  const KEY = 'lefteria-analytics-consent';
  let visible = $state(false);
  onMount(() => { try { visible = !localStorage.getItem(KEY); } catch { visible = true; } });
  function decide(value: 'granted' | 'denied') {
    try { localStorage.setItem(KEY, value); } catch { /* storage unavailable: hide for this view only */ }
    visible = false;
  }
</script>
{#if visible}
  <div class="cookie-consent" data-testid="cookie-consent" lang="el">
    <div class="cookie-consent-card">
      <div>
        <h3>Cookies & Στατιστικα</h3>
        <p>Χρησιμοποιουμε ανωνυμα cookies για να καταλαβουμε ποιες σελιδες προτιμας και να βελτιωσουμε την εμπειρια σου. Δεν μοιραζομαστε τα δεδομενα σου με τριτους. Μπορεις να αλλαξεις την επιλογη σου οποτεδηποτε.</p>
      </div>
      <div class="cookie-consent-actions">
        <button type="button" class="cookie-consent-accept" onclick={() => decide('granted')} data-testid="consent-accept">Αποδοχη</button>
        <button type="button" class="cookie-consent-decline" onclick={() => decide('denied')} data-testid="consent-decline">Απορριψη</button>
      </div>
    </div>
  </div>
{/if}
