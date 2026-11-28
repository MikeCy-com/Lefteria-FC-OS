<script lang="ts">
  import { onMount } from 'svelte';

  type Review = {
    registration_id: string;
    player_id?: string;
    status: 'not_verified' | 'verified_proof' | 'revoked';
    version?: number;
    phone_hint?: string;
    proof_method?: string;
    reviewed_at?: string;
    revoked_at?: string;
    app_access_enabled: false;
  };
  let { registrationId, guardianPhone, expectedPlayerId=null }:
    { registrationId: string; guardianPhone: string; expectedPlayerId?:string|null } = $props();
  const endpoint = '/api/admin/academy/office/guardians';
  let review = $state<Review | null>(null);
  let busy = $state(false);
  let error = $state('');
  let notice = $state('');
  let method = $state('');
  let caseReference = $state('');
  let attested = $state(false);
  let requestId = $state('');
  let generation = 0;
  const normalizedPhone = $derived(guardianPhone.replace(/[^0-9]/g, ''));
  const validPhone = $derived(/^[1-9][0-9]{7,14}$/.test(normalizedPhone));

  async function refresh() {
    const current = ++generation;
    error = '';
    try {
      const response = await fetch(endpoint+'?registration_id='+encodeURIComponent(registrationId),
        { credentials:'same-origin', cache:'no-store' });
      if (!response.ok) throw new Error('Ο έλεγχος κηδεμόνα δεν είναι διαθέσιμος.');
      const data: unknown = await response.json();
      if (!data || typeof data !== 'object' || Array.isArray(data)) throw new Error();
      const result = data as Review;
      if (result.registration_id !== registrationId ||
        (expectedPlayerId!==null&&result.player_id!==expectedPlayerId) ||
        !['not_verified','verified_proof','revoked'].includes(result.status) ||
        result.app_access_enabled !== false) throw new Error();
      if (current === generation) review = result;
    } catch {
      if (current === generation) error = 'Δεν ήταν δυνατή η φόρτωση του ελέγχου. Δοκιμάστε ξανά.';
    }
  }
  onMount(() => {
    void refresh();
    return () => { generation++; };
  });

  async function verify(event: SubmitEvent) {
    event.preventDefault();
    if (busy || !validPhone || !attested ||
      !['in_person_document','court_guardianship_order'].includes(method) ||
      !/^[A-Za-z0-9][A-Za-z0-9_-]{7,63}$/.test(caseReference)) return;
    busy = true; error = ''; notice = '';
    requestId ||= crypto.randomUUID();
    try {
      const response = await fetch(endpoint+'/verify', {
        method:'POST', credentials:'same-origin', cache:'no-store',
        headers:{ 'Content-Type':'application/json', 'X-Lefteria-Admin':'1' },
        body:JSON.stringify({ registration_id:registrationId, phone:normalizedPhone,
          proof_method:method,case_reference:caseReference,
          request_id:requestId,attested:true })
      });
      if (!response.ok) {
        if (response.status===409) throw new Error('Ο φάκελος άλλαξε. Ελέγξτε ξανά την κατάστασή του.');
        throw new Error('Η επαλήθευση δεν καταχωρίστηκε. Ελέγξτε τα στοιχεία και την εξουσιοδότηση.');
      }
      const data: unknown = await response.json();
      if (!data || typeof data !== 'object' || Array.isArray(data) ||
        (data as Record<string,unknown>).status !== 'verified_proof' ||
        (data as Record<string,unknown>).registration_id !== registrationId ||
        (expectedPlayerId!==null&&(data as Record<string,unknown>).player_id!==expectedPlayerId) ||
        (data as Record<string,unknown>).app_access_enabled !== false)
        throw new Error('Η καταχώριση δεν επιβεβαιώθηκε.');
      notice = 'Ο ανθρώπινος έλεγχος τεκμηριώθηκε. Δεν έχει ενεργοποιηθεί πρόσβαση στην εφαρμογή.';
      requestId = ''; attested = false;
      await refresh();
    } catch (e) { error = e instanceof Error ? e.message : 'Ο έλεγχος απέτυχε.'; }
    finally { busy = false; }
  }

  async function revoke() {
    if (busy || review?.status !== 'verified_proof' || !review.version ||
      !window.confirm('Ανάκληση της επαλήθευσης κηδεμόνα; Το ιστορικό ελέγχου θα διατηρηθεί.')) return;
    busy = true; error = ''; notice = '';
    try {
      const response = await fetch(endpoint+'/revoke',{
        method:'POST', credentials:'same-origin', cache:'no-store',
        headers:{ 'Content-Type':'application/json', 'X-Lefteria-Admin':'1' },
        body:JSON.stringify({registration_id:registrationId,version:review.version})
      });
      if (!response.ok) throw new Error('Η ανάκληση δεν επιβεβαιώθηκε. Ελέγξτε την τρέχουσα κατάσταση.');
      const data: unknown = await response.json();
      if (!data || typeof data !== 'object' || Array.isArray(data) ||
          (data as Record<string,unknown>).status !== 'revoked' ||
          (data as Record<string,unknown>).registration_id !== registrationId ||
          (expectedPlayerId!==null&&(data as Record<string,unknown>).player_id!==expectedPlayerId) ||
          (data as Record<string,unknown>).app_access_enabled !== false)
          throw new Error('Η ανάκληση δεν επιβεβαιώθηκε.');
      notice = 'Η επαλήθευση ανακλήθηκε. Το ιστορικό παραμένει.';
      await refresh();
    } catch(e) { error = e instanceof Error ? e.message : 'Η ανάκληση απέτυχε.'; }
    finally { busy = false; }
  }
</script>

<section class="proof-panel" data-testid="academy-guardian-proof-review">
  <h4>Έλεγχος στοιχείων κηδεμόνα</h4>
  <p class="proof-note">Η τηλεφωνική δήλωση στην αίτηση δεν αποτελεί ταυτοποίηση. Απαιτείται προσωπικός έλεγχος εγγράφου από εξουσιοδοτημένο Super Admin. Η καταχώριση εδώ <strong>δεν ενεργοποιεί πρόσβαση στο προφίλ παιδιού</strong>.</p>
  {#if notice}<p role="status">{notice}</p>{/if}
  {#if error}<p role="alert" class="proof-error">{error} <button type="button" disabled={busy} onclick={() => void refresh()}>Ανανέωση</button></p>{/if}
  {#if review}
    {#if review.status==='verified_proof'}
      <p>Ο ανθρώπινος έλεγχος καταχωρίστηκε. Τηλέφωνο {review.phone_hint}, μέθοδος {review.proof_method}. Πρόσβαση εφαρμογής: <strong>μη ενεργή</strong>.</p>
      <button type="button" class="admin-secondary" disabled={busy} onclick={() => void revoke()}>Ανάκληση επαλήθευσης</button>
    {:else if review.status==='revoked'}
      <p>Η επαλήθευση έχει ανακληθεί. Η επανενεργοποίηση απαιτεί νέα ελεγχόμενη διαδικασία.</p>
    {:else}
      <form onsubmit={verify}>
        <fieldset disabled={busy}>
          <legend>Ταυτοποίηση κηδεμόνα — μόνο μετά τον πραγματικό έλεγχο</legend>
          <label>Τηλέφωνο της αίτησης
            <input type="text" readonly value={normalizedPhone} aria-describedby="proof-phone-hint" />
          </label>
          <p id="proof-phone-hint" class="proof-note">Ο αριθμός χρησιμοποιείται μόνο για αντιστοίχιση με την αρχική εγκεκριμένη αίτηση.</p>
          {#if !validPhone}<p role="alert" class="proof-error">Ο αριθμός στην αίτηση δεν είναι έγκυρος για επαλήθευση.</p>{/if}
          <label>Μέθοδος ανεξάρτητου ελέγχου
            <select required bind:value={method} onchange={() => requestId=''}>
              <option value="">Επιλέξτε μέθοδο</option>
              <option value="in_person_document">Έλεγχος επίσημου εγγράφου με φυσική παρουσία</option>
              <option value="court_guardianship_order">Επαλήθευση δικαστικής πράξης κηδεμονίας</option>
            </select>
          </label>
          <label>Εσωτερικός αριθμός φακέλου τεκμηρίωσης
            <input required minlength="8" maxlength="64" pattern={'[A-Za-z0-9][A-Za-z0-9_-]{7,63}'}
              placeholder="CASE-2026-0001" bind:value={caseReference}
              oninput={() => requestId=''} />
          </label>
          <p class="proof-note">Καταχωρίστε μόνο τον κωδικό του ήδη ελεγμένου προστατευμένου φακέλου. Μη γράφετε ευαίσθητες πληροφορίες, ιατρικά δεδομένα ή αριθμούς εγγράφων εδώ.</p>
          <label class="proof-checkbox">
            <input type="checkbox" required bind:checked={attested}
              onchange={() => requestId=''} />
            Επιβεβαιώνω ότι έλεγξα ανεξάρτητα την ταυτότητα και την κηδεμονική ιδιότητα, όχι μόνο το τηλέφωνο της δημόσιας αίτησης.
          </label>
          <button class="admin-primary" disabled={busy || !validPhone || !attested ||
            !method || !/^[A-Za-z0-9][A-Za-z0-9_-]{7,63}$/.test(caseReference)}>
            Καταχώριση ελέγχου (χωρίς άδεια εφαρμογής)
          </button>
        </fieldset>
      </form>
    {/if}
  {:else if !error}<p role="status">Φόρτωση κατάστασης ελέγχου…</p>{/if}
</section>
<style>
  .proof-panel{margin-top:1rem;padding:1rem;border:1px solid var(--admin-border,#6c6250);border-radius:12px}
  .proof-note{max-width:76ch;color:var(--admin-muted,#aeb4b9);font-size:.9rem}
  .proof-error{color:var(--admin-error,#fa9999)}
  form,fieldset{display:grid;gap:1rem;width:100%}
  fieldset{border:0;padding:0;min-width:0}
  label{display:grid;gap:.35rem;max-width:35rem}
  .proof-checkbox{display:flex;gap:.6rem;align-items:flex-start;max-width:70ch}
  .proof-checkbox input{flex-shrink:0;margin-top:.25rem}
  input,select{max-width:100%}
</style>
