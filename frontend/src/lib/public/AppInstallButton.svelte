<script lang="ts">
  import { onDestroy } from 'svelte';
  import { lockPageScroll } from './dialog-scroll';
  import { appInstall } from '../mobile/app-install';
  import Icon from './PublicIcon.svelte';
  let dialog: HTMLDialogElement;
  let releaseScroll: (() => void) | undefined;
  function restoreScroll() { releaseScroll?.(); releaseScroll = undefined; }
  onDestroy(restoreScroll);
  function showGuidance() {
    if (!dialog.open) { dialog.showModal(); releaseScroll = lockPageScroll(document); }
  }
  async function installCurrentSite() {
    const result = await appInstall.install();
    if (result === 'help') showGuidance();
    else if (result === 'prompted' && dialog.open) dialog.close();
  }
  function install() {
    // Trigger the browser-owned OS/PWA install prompt directly in the click
    // gesture. An external APK belongs to a different app and is never used.
    void installCurrentSite();
  }
</script>

{#if $appInstall.installed}
  <a href="/app" class="public-app-open" data-testid="official-app-open"><Icon name="external" size={21} />Άνοιξε την επίσημη εφαρμογή</a>
{:else}
  <button type="button" class="public-app-open" onclick={install} disabled={$appInstall.busy} aria-haspopup="dialog" data-testid="official-app-install"><Icon name="download" size={21} />{$appInstall.busy ? 'Αναμονή επιβεβαίωσης…' : 'Εγκατάσταση επίσημης εφαρμογής OS'}</button>
{/if}

<dialog bind:this={dialog} onclose={restoreScroll} aria-labelledby="app-install-title" aria-describedby="app-install-description">
  <div class="install-heading"><h2 id="app-install-title">Η επίσημη εφαρμογή Λευτεριά FC OS</h2><button type="button" onclick={() => dialog.close()} aria-label="Κλείσιμο οδηγιών εγκατάστασης">×</button></div>
  <p id="app-install-description">Εγκατέστησε την εφαρμογή του Lefteria FC OS στην αρχική οθόνη για γρήγορη πρόσβαση. Η εγκατάσταση αφορά αυτόν τον ιστότοπο.</p>
  {#if $appInstall.android}
    <section aria-label="Εγκατάσταση Lefteria FC OS στο Android">
      <h3>Επίσημη εφαρμογή Lefteria FC OS</h3>
      <p>Εγκατέστησε το OS που χρησιμοποιείς τώρα. Δεν απαιτείται λήψη APK από άλλη ιστοσελίδα.</p>
      {#if $appInstall.available}
        <button type="button" class="install-open" onclick={installCurrentSite}
          disabled={$appInstall.busy} data-testid="current-site-install">
          Εγκατάσταση τώρα
        </button>
      {/if}
    </section>
  {/if}
  {#if $appInstall.ios}
    <p>Το iPhone δεν επιτρέπει σε μια ιστοσελίδα να ανοίξει αυτόματα την επιβεβαίωση εγκατάστασης.</p>
    <ol><li>Άνοιξε αυτή τη σελίδα στο Safari.</li><li>Πάτησε <strong>Κοινοποίηση</strong> και <strong>Προσθήκη στην οθόνη Αφετηρίας</strong>.</li><li>Επιβεβαίωσε με <strong>Προσθήκη</strong> και άνοιξε το εικονίδιο Λευτεριά FC.</li></ol>
  {:else}
    <ol><li>Άνοιξε το μενού του προγράμματος περιήγησης.</li><li>Επίλεξε <strong>Εγκατάσταση εφαρμογής</strong> ή <strong>Προσθήκη στην αρχική οθόνη</strong>, εφόσον εμφανίζεται.</li><li>Επιβεβαίωσε την εγκατάσταση στη συσκευή σου.</li></ol>
    <p>Αν δεν εμφανίζεται αυτή η επιλογή, δοκίμασε Chrome ή Edge, ή συνέχισε από το πρόγραμμα περιήγησης.</p>
  {/if}
  <p class="install-note">Η εγκατάσταση εξαρτάται από τις δυνατότητες του browser και χρειάζεται τη δική σου επιβεβαίωση. Δεν χρησιμοποιείται η παλιά εφαρμογή ή εξωτερικό APK.</p>
  <a href="/app" class="install-open" onclick={() => dialog.close()}>Συνέχεια στην εφαρμογή →</a>
</dialog>

<style>
  button.public-app-open { font: inherit; font-weight: 700; cursor: pointer; background: var(--public-gold, #f6bb38); color: #090909; border-color: var(--public-gold, #f6bb38); border-radius: 5px; }
  button.public-app-open:disabled { opacity: .65; cursor: wait; }
  dialog { width: min(92vw, 540px); max-height: 85dvh; overflow: auto; padding: 24px; color: #f5f5f5; background: #101112; border: 1px solid var(--public-gold, #f6bb38); border-radius: 12px; box-shadow: 0 20px 80px #0009; }
  dialog::backdrop { background: #000b; }
  .install-heading { display: flex; align-items: flex-start; gap: 16px; }
  .install-heading h2 { margin: 0; font-size: 24px; color: #ffca46; }
  .install-heading button { margin-left: auto; min-width: 44px; min-height: 44px; background: #191b1c; border: 1px solid #777; border-radius: 6px; color: white; font-size: 28px; cursor: pointer; }
  p, li { line-height: 1.6; }
  ol { padding-left: 24px; }
  li + li { margin-top: 10px; }
  h3 { color: var(--public-gold, #f6bb38); }
  hr { border: 0; border-top: 1px solid #555; margin: 20px 0; }
  button.install-open { font: inherit; cursor: pointer; border: 0; }
  .install-note { color: #bbb; font-size: 14px; }
  .install-open { display: inline-block; padding: 12px 18px; color: #090909; background: var(--public-gold, #f6bb38); border-radius: 5px; font-weight: 700; text-decoration: none; }
  button:focus-visible, a:focus-visible { outline: 3px solid #fff; outline-offset: 3px; }
</style>
