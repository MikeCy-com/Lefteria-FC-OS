<script lang="ts">
  import { appInstall } from '../mobile/app-install';
  import Icon from './PublicIcon.svelte';
  let dialog: HTMLDialogElement;
  async function install() {
    const result = await appInstall.install();
    if (result === 'help') dialog.showModal();
  }
</script>

{#if $appInstall.installed}
  <a href="/app" class="public-app-open" data-testid="official-app-open"><Icon name="external" size={21} />Άνοιξε την επίσημη εφαρμογή</a>
{:else}
  <button type="button" class="public-app-open" onclick={install} disabled={$appInstall.busy} aria-haspopup="dialog" data-testid="official-app-install"><Icon name="download" size={21} />{$appInstall.busy ? 'Αναμονή επιβεβαίωσης…' : 'Κατέβασε την επίσημη εφαρμογή'}</button>
{/if}

<dialog bind:this={dialog} aria-labelledby="app-install-title" aria-describedby="app-install-description">
  <div class="install-heading"><h2 id="app-install-title">Η επίσημη εφαρμογή Λευτεριά FC</h2><button type="button" onclick={() => dialog.close()} aria-label="Κλείσιμο οδηγιών εγκατάστασης" autofocus>×</button></div>
  <p id="app-install-description">Πρόσθεσε την εφαρμογή στην αρχική οθόνη για γρήγορη πρόσβαση στον λογαριασμό σου.</p>
  {#if $appInstall.ios}
    <ol><li>Άνοιξε αυτή τη σελίδα στο Safari.</li><li>Πάτησε <strong>Κοινοποίηση</strong> και <strong>Προσθήκη στην οθόνη Αφετηρίας</strong>.</li><li>Επιβεβαίωσε με <strong>Προσθήκη</strong> και άνοιξε το εικονίδιο Λευτεριά FC.</li></ol>
  {:else}
    <ol><li>Άνοιξε το μενού του προγράμματος περιήγησης.</li><li>Επίλεξε <strong>Εγκατάσταση εφαρμογής</strong> ή <strong>Προσθήκη στην αρχική οθόνη</strong>, εφόσον εμφανίζεται.</li><li>Επιβεβαίωσε την εγκατάσταση στη συσκευή σου.</li></ol>
    <p>Αν δεν εμφανίζεται αυτή η επιλογή, δοκίμασε Chrome ή Edge, ή συνέχισε από το πρόγραμμα περιήγησης.</p>
  {/if}
  <p class="install-note">Η εγκατάσταση χρειάζεται επιβεβαίωση από εσένα. Μπορείς πάντα να χρησιμοποιείς την εφαρμογή από το πρόγραμμα περιήγησης.</p>
  <a href="/app" class="install-open" onclick={() => dialog.close()}>Συνέχεια στην εφαρμογή →</a>
</dialog>

<style>
  button.public-app-open { font: inherit; cursor: pointer; }
  button.public-app-open:disabled { opacity: .65; cursor: wait; }
  dialog { width: min(92vw, 540px); max-height: 85dvh; overflow: auto; padding: 24px; color: #f5f5f5; background: #101112; border: 1px solid #f5a623; border-radius: 12px; box-shadow: 0 20px 80px #0009; }
  dialog::backdrop { background: #000b; }
  .install-heading { display: flex; align-items: flex-start; gap: 16px; }
  .install-heading h2 { margin: 0; font-size: 24px; color: #ffca46; }
  .install-heading button { margin-left: auto; min-width: 44px; min-height: 44px; background: #191b1c; border: 1px solid #777; border-radius: 6px; color: white; font-size: 28px; cursor: pointer; }
  p, li { line-height: 1.6; }
  ol { padding-left: 24px; }
  li + li { margin-top: 10px; }
  .install-note { color: #bbb; font-size: 14px; }
  .install-open { display: inline-block; padding: 12px 18px; color: #090909; background: #f5a623; border-radius: 5px; font-weight: 700; text-decoration: none; }
  button:focus-visible, a:focus-visible { outline: 3px solid #fff; outline-offset: 3px; }
</style>
