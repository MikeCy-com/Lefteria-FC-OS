<script lang="ts">
  import { untrack } from 'svelte';
  import type { components } from '../../api/schema';
  import './mobile-theme.css';

  type Family = components['schemas']['ParentFamilyResult'];
  type OutgoingInvitations = components['schemas']['ParentOutgoingInvitationsResult'];
  type ActiveDelegations = components['schemas']['ParentActiveDelegationsResult'];
  type GuardianPermission = {
    id:string;player_id:string;organization_id:string;kind:'guardian';
    status:'granted';expires_at:null;recipient_hint:string
  };
  type GuardianPermissions = {items:GuardianPermission[]};

  interface Props {
    family: Family | null;
    guardianPermissions?: GuardianPermissions|null;
    guardianStatus?: 'idle'|'loading'|'ready'|'error';
    guardianError?: string|null;
    guardianPending?: boolean;
    guardianRevokingId?: string|null;
    onLoadGuardianPermissions?: ()=>boolean|Promise<boolean>;
    onCreatePermission?: (playerId:string,kind:'player'|'guardian',
      phone:string,requestId:string)=>boolean|Promise<boolean>;
    onCreateManyGuardian?: (children:Array<{player_id:string;request_id:string}>,
      phone:string)=>boolean|Promise<boolean>;
    onRevokeGuardianPermission?: (id:string)=>boolean|Promise<boolean>;
    status: 'idle' | 'loading' | 'ready' | 'error';
    error: string | null;
    busy?: boolean;
    onLoad: () => boolean | Promise<boolean>;
    onCalendar: (playerId: string) => void | Promise<void>;
    outgoingInvitations?: OutgoingInvitations | null;
    outgoingStatus?: 'idle' | 'loading' | 'ready' | 'error';
    outgoingError?: string | null;
    onLoadOutgoing?: () => boolean | Promise<boolean>;
    onCancelOutgoing?: (invitationId: string) => boolean | Promise<boolean>;
    outgoingCancellingId?: string | null;
    outgoingCancelError?: string | null;
    onRevokeDelegation?: (playerId: string) => boolean | Promise<boolean>;
    revokingPlayerId?: string | null;
    revokeError?: string | null;
    revokeSuccess?: string | null;
    activeDelegations?: ActiveDelegations | null;
    activeStatus?: 'idle' | 'loading' | 'ready' | 'error';
    activeError?: string | null;
    onLoadActive?: () => boolean | Promise<boolean>;
  }

  let { family, status, error, busy = false, onLoad, onCalendar,
    guardianPermissions=null,guardianStatus='idle',guardianError=null,
    guardianPending=false,guardianRevokingId=null,onLoadGuardianPermissions,
    onCreatePermission,onCreateManyGuardian,onRevokeGuardianPermission,
    outgoingInvitations = null, outgoingStatus = 'idle', outgoingError = null,
    onLoadOutgoing, onCancelOutgoing, outgoingCancellingId = null,
    outgoingCancelError = null, onRevokeDelegation,
    revokingPlayerId = null, revokeError = null, revokeSuccess = null,
    activeDelegations = null, activeStatus = 'idle', activeError = null,
    onLoadActive }: Props = $props();
  let permissionType=$state<'player'|'guardian'>('player');
  let permissionChild=$state('');
  let permissionPhone=$state('');
  let requestId=$state('');
  let bulkRequest=$state<{
    key:string;items:Array<{player_id:string;request_id:string}>
  }|null>(null);
  function clearPermissionDraft(){
    requestId='';bulkRequest=null;permissionNotice='';
  }
  let permissionNotice=$state('');
  let confirmingGuardianId=$state<string|null>(null);
  let confirmingId = $state<string | null>(null);
  let confirmingRevokeId = $state<string | null>(null);

  // One child is automatically selected. Each additional child can
  // be granted separately, or all children can be selected together for
  // the same second parent. Individual records remain revocable.
  const resolvedChild=$derived(family?.children.length===1
    ? family.children[0].id : permissionChild);
  let submitting=$state(false);
  async function createPermission() {
    if (!onCreatePermission || guardianPending || submitting || busy || !family ||
      !/^\+?[0-9 ()-]{8,24}$/.test(permissionPhone)) return;
    const children=permissionType==='guardian'&&resolvedChild==='all'
      ?family.children
      :family.children.filter(child=>child.id===resolvedChild);
    if(children.length===0)return;
    submitting=true;permissionNotice='';
    try{
      if(children.length>1){
        if(!onCreateManyGuardian||permissionType!=='guardian')return;
        const key=permissionPhone.trim()+':'+children.map(ch=>ch.id).join(',');
        if(bulkRequest?.key!==key)bulkRequest={
          key,items:children.map(child=>({
            player_id:child.id,request_id:crypto.randomUUID()
          }))
        };
        const ok=await onCreateManyGuardian(bulkRequest.items,permissionPhone);
        if(ok){
          permissionPhone='';clearPermissionDraft();
          permissionNotice='Η άδεια καταχωρίστηκε για όλα τα παιδιά. Ο παραλήπτης συνδέεται με επαληθευμένο κινητό.';
        }
      }else{
        const id=requestId||crypto.randomUUID();
        requestId=id;
        const ok=await onCreatePermission(children[0].id,permissionType,permissionPhone,id);
        if(ok){
          permissionPhone='';requestId='';
          permissionNotice=permissionType==='player'
            ?'Η άδεια παίκτη καταχωρίστηκε για το επιλεγμένο παιδί.'
            :'Η άδεια κηδεμόνα αποθηκεύτηκε. Ο παραλήπτης συνδέεται με επαληθευμένο κινητό.';
        }
      }
    }finally{submitting=false;}
  }
  async function confirmGuardianRevoke(id:string){
    if(!onRevokeGuardianPermission||guardianRevokingId)return;
    if(await onRevokeGuardianPermission(id)){
      confirmingGuardianId=null;
      permissionNotice='Η άδεια ανακλήθηκε.';
    }
  }
  async function confirmCancel(id: string) {
    if (!onCancelOutgoing || outgoingCancellingId) return;
    if (await onCancelOutgoing(id)) confirmingId = null;
  }
  async function confirmRevoke(childId: string) {
    if (!onRevokeDelegation || revokingPlayerId) return;
    if (await onRevokeDelegation(childId)) confirmingRevokeId = null;
  }
  const id = $props.id();

  // A newly mounted parent view refreshes the canonical server relationship.
  // No locally cached player or browser-provided child ID is treated as a grant.
  $effect(() => {
    if (status === 'idle' && !busy) untrack(() => { void onLoad(); });
  });

  $effect(() => {
    if (status === 'ready' && family && activeStatus === 'idle' && onLoadActive && !busy) {
      untrack(() => { void onLoadActive(); });
    }
  });

  $effect(()=>{
    if(status==='ready'&&family&&guardianStatus==='idle'&&
      onLoadGuardianPermissions&&!busy){
      untrack(()=>{void onLoadGuardianPermissions();});
    }
  });

  // Never load outgoing records while the guardian-child source is missing.
  // Recheck the current parent identity and guardian role in the controller.
  $effect(() => {
    if (status === 'ready' && family && outgoingStatus === 'idle' && onLoadOutgoing && !busy) {
      untrack(() => { void onLoadOutgoing(); });
    }
  });

  function organizationLabel(value: string): string {
    return value === 'lefteria_fc_academy' ? 'ΑΚΑΔΗΜΙΑ' : 'ΛΕΥΤΕΡΙΑ FC';
  }
  function teamTypeLabel(value: string): string {
    return value === 'Academy' ? 'Ακαδημία' : 'Πρώτη ομάδα';
  }
</script>

<section class="family-page" aria-labelledby={id + '-title'} data-testid="mobile-family">
  <header class="family-heading">
    <p>ΟΙΚΟΓΕΝΕΙΑ · ΛΕΥΤΕΡΙΑ FC</p>
    <h1 id={id + '-title'}>Τα παιδιά μου</h1>
    <span>Επαληθευμένες συνδέσεις γονέα και αθλητών</span>
  </header>

  {#if status === 'loading' || status === 'idle'}
    <div class="family-notice" role="status" data-testid="family-loading">Φόρτωση οικογενειακών στοιχείων…</div>
  {:else if status === 'error'}
    <div class="family-notice family-error" role="alert" data-testid="family-unavailable">
      <p>{error ?? 'Τα οικογενειακά στοιχεία δεν είναι προσωρινά διαθέσιμα.'}</p>
      <button type="button" disabled={busy} onclick={() => void onLoad()}>Δοκιμή ξανά</button>
    </div>
  {:else if family}
    {#if family.children.length>0}
      <section class="family-invitations family-permission-editor"
        aria-labelledby={id+'-permissions'} data-testid="family-permission-editor">
        <h2 id={id+'-permissions'}>Δώσε άδεια πρόσβασης</h2>
        <p>Πρόσθεσε ξεχωριστή πρόσβαση για παιδί ή δεύτερο γονέα / κηδεμόνα.</p>
        <form class="family-permission-fields"
          onsubmit={(e)=>{e.preventDefault();void createPermission();}}>
          <label>Δώσε άδεια σε
            <select bind:value={permissionType} disabled={busy||guardianPending||submitting}
              onchange={()=>{permissionChild='';clearPermissionDraft();}}>
              <option value="player">Παίκτη — μόνο το δικό του προφίλ</option>
              <option value="guardian">Γονέα / Κηδεμόνα</option>
            </select>
          </label>
          {#if family.children.length===1}
            <p class="family-single-child">Παιδί: <strong>{family.children[0].name}</strong></p>
          {:else}
            <label>Επίλεξε παιδί
              <select bind:value={permissionChild} disabled={busy||guardianPending||submitting}
                required onchange={()=>{clearPermissionDraft();}}>
                <option value="">Επίλεξε παιδί</option>
                {#if permissionType==='guardian'}
                  <option value="all">Όλα τα παιδιά μου</option>
                {/if}
                {#each family.children as child (child.id)}
                  <option value={child.id}>{child.name}</option>
                {/each}
              </select>
            </label>
          {/if}
          <label>Αριθμός κινητού με κωδικό χώρας
            <input type="tel" autocomplete="tel" inputmode="tel"
              bind:value={permissionPhone} maxlength="24" required
              placeholder="+357 99 123456" disabled={busy||guardianPending||submitting}
              oninput={()=>{clearPermissionDraft();}}/>
          </label>
          <button type="submit" disabled={busy||guardianPending||submitting||
            !resolvedChild||!permissionPhone.trim()}>
            {guardianPending||submitting?'Καταχώριση…':'Δώσε άδεια'}
          </button>
        </form>
        <p>Ο παίκτης βλέπει μόνο το δικό του προφίλ. Η άδεια γονέα
          αφορά μόνο τα παιδιά που επιλέχθηκαν. Το κινητό του παραλήπτη
          επαληθεύεται κατά τη σύνδεσή του στην εφαρμογή.</p>
        {#if permissionNotice}<p role="status">{permissionNotice}</p>{/if}
        {#if guardianError}<p role="alert">{guardianError}</p>{/if}
      </section>
      <section class="family-invitations" data-testid="family-guardian-permissions">
        <h2>Όλες οι άδειες πρόσβασης</h2>
        <p>Άδειες παίκτη και γονέα / κηδεμόνα για κάθε παιδί, με ξεχωριστή ανάκληση.</p>
        {#if outgoingStatus==='ready' && outgoingInvitations}
          {#each outgoingInvitations.items.filter(item=>
            family.children.some(child=>child.id===item.player_id&&
              child.organization_id===item.organization_id)) as invitation (invitation.id)}
            {@const child=family.children.find(ch=>ch.id===invitation.player_id)}
            <article class="family-outgoing-row" data-testid="family-player-permission">
              <strong>{child?.name??'Παιδί'} · Παίκτης</strong>
              <p>Εκκρεμεί η αποδοχή της πρόσκλησης.</p>
              {#if onCancelOutgoing}
                {#if confirmingId===invitation.id}
                  <div class="family-cancel-actions">
                    <button type="button" onclick={()=>confirmingId=null}>Όχι</button>
                    <button type="button" disabled={!!outgoingCancellingId}
                      onclick={()=>void confirmCancel(invitation.id)}>
                      Διαγραφή άδειας
                    </button>
                  </div>
                {:else}
                  <button class="family-cancel-start" type="button"
                    onclick={()=>confirmingId=invitation.id}>Διαγραφή άδειας</button>
                {/if}
              {/if}
            </article>
          {/each}
        {/if}
        {#if activeStatus==='ready' && activeDelegations}
          {#each activeDelegations.items.filter(item=>
            family.children.some(child=>child.id===item.player_id&&
              child.organization_id===item.organization_id)) as permission (permission.player_id)}
            {@const child=family.children.find(ch=>ch.id===permission.player_id)}
            <article class="family-outgoing-row" data-testid="family-active-player-permission">
              <strong>{child?.name??'Παιδί'} · Παίκτης</strong>
              <p>Ενεργή σύνδεση παίκτη</p>
              {#if onRevokeDelegation}
                {#if confirmingRevokeId===permission.player_id}
                  <div class="family-cancel-actions">
                    <button type="button" onclick={()=>confirmingRevokeId=null}>Όχι</button>
                    <button type="button" disabled={!!revokingPlayerId}
                      onclick={()=>void confirmRevoke(permission.player_id)}>
                      Διαγραφή άδειας
                    </button>
                  </div>
                {:else}
                  <button class="family-cancel-start" type="button"
                    onclick={()=>confirmingRevokeId=permission.player_id}>
                    Διαγραφή άδειας
                  </button>
                {/if}
              {/if}
            </article>
          {/each}
        {/if}
        <h3>Γονείς / Κηδεμόνες</h3>
        {#if guardianStatus==='idle'||guardianStatus==='loading'}
          <p role="status">Έλεγχος αδειών…</p>
        {:else if guardianStatus==='error'}
          <p role="alert">{guardianError||'Η λίστα αδειών δεν είναι διαθέσιμη.'}</p>
          {#if onLoadGuardianPermissions}
            <button type="button" onclick={()=>void onLoadGuardianPermissions()}>
              Επανάληψη
            </button>
          {/if}
        {:else if guardianPermissions?.items.length===0}
          <p>Δεν υπάρχουν ενεργές ή εκκρεμείς άδειες άλλου κηδεμόνα.</p>
        {:else if guardianPermissions}
          <div class="family-outgoing-list">
            {#each guardianPermissions.items as permission (permission.id)}
              {@const child=family.children.find(item=>item.id===permission.player_id)}
              <article class="family-outgoing-row" data-testid="family-guardian-permission">
                <strong>{child?.name??'Παιδί'} · Γονέας / Κηδεμόνας · {permission.recipient_hint}</strong>
                <p>{'Άδεια στο κινητό · απαιτείται επαληθευμένη σύνδεση'}</p>
                {#if onRevokeGuardianPermission}
                  {#if confirmingGuardianId===permission.id}
                    <p>Να ανακληθεί η πρόσβαση για αυτό το παιδί;</p>
                    <div class="family-cancel-actions">
                      <button type="button" onclick={()=>confirmingGuardianId=null}>
                        Όχι
                      </button>
                      <button type="button" disabled={!!guardianRevokingId}
                        onclick={()=>void confirmGuardianRevoke(permission.id)}>
                        Ναι, ανάκληση
                      </button>
                    </div>
                  {:else}
                    <button type="button" class="family-cancel-start"
                      onclick={()=>confirmingGuardianId=permission.id}>
                      Διαγραφή άδειας
                    </button>
                  {/if}
                {/if}
              </article>
            {/each}
          </div>
        {/if}
      </section>
    {/if}
    {#if family.children.length === 0}
      <p class="family-notice" data-testid="family-empty">Δεν υπάρχουν επαληθευμένες συνδέσεις παιδιών για αυτόν τον λογαριασμό.</p>
    {:else}
      <div class="family-children" data-testid="family-children">
        {#each family.children as child (child.id)}
          <article class="family-child" data-testid="family-child">
            <div class="family-child-avatar" aria-hidden="true">{child.name.trim().charAt(0).toLocaleUpperCase('el-GR')}</div>
            <div class="family-child-body">
              <p class="family-organization">{organizationLabel(child.organization_id)}</p>
              <h2>{child.name}</h2>
              <p>{teamTypeLabel(child.team_type)}</p>
              <button type="button" disabled={busy} onclick={() => void onCalendar(child.id)}
                class="family-calendar-link" aria-label={`Πρόγραμμα για ${child.name}`} data-testid="family-child-calendar">
                Το πρόγραμμα <span aria-hidden="true">→</span>
              </button>
              {#if onRevokeDelegation && activeStatus === 'ready' && activeDelegations?.items.some(item => item.player_id === child.id && item.organization_id === child.organization_id)}
                {#if confirmingRevokeId === child.id}
                  <div class="family-cancel-confirm" data-testid="family-revoke-confirm">
                    <p>Να ανακληθεί η ενεργή πρόσβαση οικογενειακής σύνδεσης για {child.name};</p>
                    <div class="family-cancel-actions">
                      <button type="button" disabled={busy || !!revokingPlayerId}
                        data-testid="family-revoke-back" onclick={() => { confirmingRevokeId = null; }}>Όχι</button>
                      <button type="button" disabled={busy || !!revokingPlayerId}
                        data-testid="family-revoke-submit" onclick={() => void confirmRevoke(child.id)}>
                        {revokingPlayerId === child.id ? 'Ανάκληση…' : 'Ναι, ανάκληση'}
                      </button>
                    </div>
                  </div>
                {:else}
                  <button type="button" class="family-cancel-start"
                    disabled={busy || !!revokingPlayerId} data-testid="family-revoke-start"
                    aria-label={'Ανάκληση οικογενειακής πρόσβασης για ' + child.name}
                    onclick={() => { confirmingRevokeId = child.id; }}>Ανάκληση οικογενειακής πρόσβασης</button>
                {/if}
              {/if}
            </div>
          </article>
        {/each}
      </div>
    {/if}
    {#if onLoadActive && (activeStatus === 'loading' || activeStatus === 'idle')}
      <p role="status" class="family-outgoing-status" data-testid="family-active-loading">Έλεγχος ενεργών οικογενειακών συνδέσεων…</p>
    {:else if onLoadActive && activeStatus === 'error'}
      <div class="family-outgoing-error" role="alert" data-testid="family-active-error">
        <p>{activeError || 'Δεν ήταν δυνατός ο έλεγχος ενεργών συνδέσεων.'}</p>
        <button type="button" disabled={busy} onclick={() => void onLoadActive()}>Δοκιμή ξανά</button>
      </div>
    {/if}
    {#if revokeError}
      <p class="family-notice family-error" role="alert" data-testid="family-revoke-error">{revokeError}</p>
    {/if}
    {#if revokeSuccess}
      <p class="family-notice" role="status" data-testid="family-revoke-success">{revokeSuccess}</p>
    {/if}
    {#if onLoadOutgoing}
      <section class="family-invitations" aria-labelledby={id + '-outgoing'} data-testid="family-outgoing-invitations">
        <h2 id={id + '-outgoing'}>Εκκρεμείς προσκλήσεις</h2>
        {#if outgoingStatus === 'idle' || outgoingStatus === 'loading'}
          <p class="family-outgoing-status" role="status" data-testid="family-outgoing-loading">Έλεγχος εκκρεμών αιτημάτων…</p>
        {:else if outgoingStatus === 'error'}
          <div class="family-outgoing-error" role="alert" data-testid="family-outgoing-error">
            <p>{outgoingError || 'Δεν είναι δυνατή η φόρτωση των προσκλήσεων.'}</p>
            <button type="button" disabled={busy} onclick={() => void onLoadOutgoing()}>Δοκιμή ξανά</button>
          </div>
        {:else if outgoingInvitations}
          {#if outgoingInvitations.items.length === 0}
            <p class="family-outgoing-status" data-testid="family-outgoing-empty">Δεν υπάρχουν εκκρεμείς καταχωρίσεις.</p>
          {:else}
            <div class="family-outgoing-list" data-testid="family-outgoing-list">
              {#each outgoingInvitations.items.filter(item => family.children.some(child => child.id === item.player_id && child.organization_id === item.organization_id)) as item (item.id)}
                {@const child = family.children.find(child => child.id === item.player_id)}
                <article class="family-outgoing-row" data-testid="family-outgoing-row">
                  <strong>{child?.name ?? 'Παιδί'}</strong>
                  <p>Καταχώριση σε αναμονή · {new Intl.DateTimeFormat('el-CY', { dateStyle: 'medium', timeZone: 'Asia/Nicosia' }).format(new Date(item.expires_at))}</p>
                  <span>Δεν έχει αποσταλεί πρόσκληση ή SMS.</span>
                  {#if onCancelOutgoing}
                    {#if confirmingId === item.id}
                      <div class="family-cancel-confirm" data-testid="family-cancel-confirm">
                        <p>Να ακυρωθεί η εκκρεμής καταχώριση για {child?.name ?? 'το παιδί'};</p>
                        <div class="family-cancel-actions">
                          <button type="button" disabled={busy || !!outgoingCancellingId}
                            onclick={() => { confirmingId = null; }} data-testid="family-cancel-back">Όχι</button>
                          <button type="button" disabled={busy || !!outgoingCancellingId}
                            onclick={() => void confirmCancel(item.id)} data-testid="family-cancel-submit">
                            {outgoingCancellingId === item.id ? 'Ακύρωση…' : 'Ναι, ακύρωση'}
                          </button>
                        </div>
                      </div>
                    {:else}
                      <button type="button" class="family-cancel-start"
                        disabled={busy || !!outgoingCancellingId}
                        onclick={() => { confirmingId = item.id; }}
                        aria-label={'Ακύρωση εκκρεμούς πρόσκλησης για ' + (child?.name ?? 'παιδί')}
                        data-testid="family-cancel-start">Ακύρωση καταχώρισης</button>
                    {/if}
                  {/if}
                </article>
              {/each}
            </div>
          {/if}
        {/if}
        {#if outgoingCancelError}
          <div class="family-outgoing-error" role="alert" data-testid="family-cancel-error">{outgoingCancelError}</div>
        {/if}
        <div class="family-invitations-pending">
          <span>Οι εκκρεμείς προσκλήσεις δεν παρέχουν πρόσβαση έως ότου ο παραλήπτης ολοκληρώσει την προβλεπόμενη επαλήθευση.</span>
        </div>
      </section>
    {:else}
      <div class="family-invitations" data-testid="family-invitations-unavailable">
        <h2>Οικογενειακές ρυθμίσεις</h2>
        <p>Under Developments</p>
        <span>Οι προσκλήσεις, η σύνδεση νέου παιδιού και η ανάκληση πρόσβασης θα ενεργοποιηθούν μετά τον έλεγχο ταυτότητας και ασφάλειας.</span>
      </div>
    {/if}
  {/if}
</section>

<style>
  .family-permission-fields{display:grid;gap:12px;margin:14px 0}
  .family-permission-fields label{display:grid;gap:6px;color:#eee}
  .family-permission-fields input,.family-permission-fields select{
    min-height:44px;max-width:100%;padding:10px;border-radius:6px;
    border:1px solid #55462a;background:#151717;color:#fff}
  .family-permission-fields button{min-height:44px;padding:10px;border-radius:6px;
    background:#372911;color:#ffca46;border:1px solid #a77520}
  .family-page { max-width: 850px; margin: 0 auto; color: #f4f4f4; }
  .family-heading { margin: 0 0 26px; }
  .family-heading > p { color: #ffca46; font-size: 11px; font-weight: 700; letter-spacing: .10em; margin: 0 0 8px; }
  .family-heading h1 { font-family: 'Fira Sans Extra Condensed', sans-serif; font-size: clamp(31px, 6vw, 43px); line-height: 1.1; margin: 0 0 8px; }
  .family-heading span { font-size: 14px; color: #adb2b3; }
  .family-notice { padding: 22px; border: 1px solid #303333; border-radius: 8px; background: #141717; color: #c5c8c9; line-height: 1.6; }
  .family-error { border-color: #bc5c4f; }
  .family-error p { margin: 0 0 16px; }
  .family-error button, .family-calendar-link { min-height: 44px; border: 1px solid #a77520; padding: 10px 16px; background: #251d11; color: #ffca46; border-radius: 5px; cursor: pointer; font-weight: 600; }
  .family-error button:disabled, .family-calendar-link:disabled { opacity: .55; cursor: wait; }
  .family-error button:focus-visible, .family-calendar-link:focus-visible { outline: 2px solid #ffca46; outline-offset: 3px; }
  .family-children { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 270px), 1fr)); gap: 14px; }
  .family-child { min-width: 0; display: flex; align-items: start; gap: 15px; padding: 21px; border: 1px solid #38332a; border-radius: 10px; background: linear-gradient(145deg, #1c1c1b, #101313); }
  .family-child-avatar { display: grid; place-items: center; flex: 0 0 48px; width: 48px; height: 48px; background: #332612; border: 1px solid #8b681e; border-radius: 50%; color: #ffca46; font-size: 21px; font-weight: 700; }
  .family-child-body { min-width: 0; flex: 1; overflow-wrap: anywhere; }
  .family-organization { margin: 1px 0 7px; color: #ffca46; font-size: 11px; font-weight: 700; }
  .family-child h2 { font-family: 'Fira Sans Extra Condensed', sans-serif; font-size: 25px; margin: 0 0 7px; }
  .family-child-body > p:not(.family-organization) { font-size: 13px; color: #c2c5c6; margin: 0 0 14px; }
  .family-calendar-link { padding: 10px 13px; font-size: 13px; }
  .family-invitations { margin-top: 22px; padding: 22px; border: 1px solid #343435; border-radius: 8px; background: #121415; }
  .family-invitations h2 { font-size: 20px; margin: 0 0 8px; }
  .family-outgoing-list { display: grid; gap: 10px; margin: 14px 0 20px; }
  .family-outgoing-row { border: 1px solid #4e3b20; background: #211a10; padding: 14px; border-radius: 8px; }
  .family-outgoing-row strong { color: #f6f6f6; font-size: 14px; }
  .family-outgoing-row p { color: #ffca46; font-size: 12px; margin: 7px 0; }
  .family-outgoing-row span { color: #bcb4a7; font-size: 12px; }
  .family-outgoing-status { color: #b8bdbe; font-size: 13px; line-height: 1.65; }
  .family-outgoing-error { border: 1px solid #934840; border-radius: 6px; padding: 12px; }
  .family-outgoing-error button { min-height: 44px; border: 1px solid #a77520; background: #251d11; color: #ffca46; border-radius: 5px; cursor: pointer; padding: 9px 14px; }
  .family-invitations-pending { margin-top: 15px; border-top: 1px solid #303333; padding-top: 16px; }
  .family-cancel-start { display: block; margin-top: 14px; min-height: 44px; border: 1px solid #82513c; border-radius: 6px; background: #302018; padding: 10px 14px; color: #f7b9a3; cursor: pointer; font-size: 12px; }
  .family-cancel-confirm { margin-top: 14px; padding: 12px; border: 1px solid #925844; border-radius: 6px; }
  .family-cancel-confirm p { margin: 0 0 10px; color: #f0d0c5; font-size: 12px; }
  .family-cancel-actions { display: flex; flex-wrap: wrap; gap: 10px; }
  .family-cancel-actions button { min-height: 44px; padding: 9px 14px; border: 1px solid #775c3f; border-radius: 6px; color: #f7e1ba; background: #201c16; cursor: pointer; }
  .family-cancel-actions button:last-child { border-color: #ae5f49; background: #3c1c17; color: #ffd0c5; }
  .family-cancel-actions button:disabled,.family-cancel-start:disabled { opacity: .6; cursor: wait; }
  .family-cancel-start:focus-visible,.family-cancel-actions button:focus-visible { outline: 2px solid #ffca46; outline-offset: 3px; }

  .family-invitations p { margin: 0 0 8px; font-size: 13px; color: #ffca46; font-weight: 700; }
  .family-invitations span { font-size: 13px; color: #a6abac; line-height: 1.65; }
</style>
