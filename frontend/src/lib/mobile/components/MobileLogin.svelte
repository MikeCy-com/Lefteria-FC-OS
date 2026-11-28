<script lang="ts">
  import { tick } from 'svelte';
  import { ROLE_NAMES, TAB_NAMES } from '../navigation.ts';
  import { roleCanResumeTab } from '../guest-navigation.ts';
  import type { MobileRole, MobileTab } from '../navigation.ts';
  import type { FanRegistrationForm, MobileIdentityOption, MobileLoginStep, RememberDays, UiAction } from './component-types.ts';
  import MobileIcon from './MobileIcon.svelte';
  import './mobile-theme.css';
  import { publicImages } from '../../public/images.ts';

  interface Props {
    step?: MobileLoginStep;
    phone?: string;
    rememberDays?: RememberDays;
    identities?: readonly MobileIdentityOption[];
    detectedRole?: MobileRole | null;
    busy?: boolean;
    error?: string | null;
    resendSeconds?: number;
    returnTab?: MobileTab | null;
    onRequestOtp: (phone: string) => UiAction;
    onVerifyOtp: (code: string, rememberDays: RememberDays) => UiAction;
    onChooseRole: (role: MobileRole, rememberDays: RememberDays) => UiAction;
    onRegisterFan: (registration: FanRegistrationForm) => UiAction;
    onStepChange: (step: MobileLoginStep) => void;
  }

  let {
    step = 'phone', phone = $bindable(''), rememberDays = $bindable<RememberDays>(null),
    identities = [], detectedRole = null, busy = false, error = null, resendSeconds = 0, returnTab = null,
    onRequestOtp, onVerifyOtp, onChooseRole, onRegisterFan, onStepChange
  }: Props = $props();
  const id = $props.id();
  let code = $state('');
  let firstName = $state('');
  let lastName = $state('');
  let birthday = $state('');
  let submitting = $state(false);
  // Visual audience choice does not assign an account role. Server-verified
  // identities after OTP remain the sole source of access permissions.
  let selectedAudience = $state<'player'|'parent'|'fan'>('player');
  let phoneInput = $state<HTMLInputElement>();
  let otpInput = $state<HTMLInputElement>();
  let firstNameInput = $state<HTMLInputElement>();
  let roleHeading = $state<HTMLHeadingElement>();
  const isBusy = $derived(busy || submitting);

  $effect(() => {
    const current = step;
    void tick().then(() => {
      if (current !== step) return;
      if (current === 'phone') phoneInput?.focus();
      if (current === 'otp') otpInput?.focus();
      if (current === 'fan_register') firstNameInput?.focus();
      if (current === 'choose_role') roleHeading?.focus();
    });
  });

  async function run(action: () => UiAction) {
    if (isBusy) return;
    submitting = true;
    try { await action(); } finally { submitting = false; }
  }

  function changeStep(next: MobileLoginStep) {
    if (isBusy) return;
    code = '';
    onStepChange(next);
  }

  function selectRemember(value: string) {
    rememberDays = value === '30' ? 30 : value === '60' ? 60 : value === '90' ? 90 : null;
  }
</script>

{#snippet rememberChoice()}
  <div class="remember-choice">
    <label for={`${id}-remember`}>Διάρκεια σύνδεσης</label>
    <select id={`${id}-remember`} value={rememberDays === null ? '' : String(rememberDays)}
      onchange={(event) => selectRemember(event.currentTarget.value)} disabled={isBusy} data-testid="remember-days">
      <option value="">24 ώρες</option><option value="30">30 ημέρες</option>
      <option value="60">60 ημέρες</option><option value="90">90 ημέρες</option>
    </select>
    <p>Επιλέξτε μεγαλύτερη διάρκεια μόνο σε προσωπική συσκευή.</p>
  </div>
{/snippet}

<section class="lf-mobile-theme login-page" aria-busy={isBusy} data-testid="mobile-login">
  <div class="login-platform">
    <aside class="login-sidebar" aria-label="Πλοήγηση ιστοσελίδας">
      <a href="/" aria-label="Λευτεριά FC · Αρχική"><img src="/mobile/club-logo.png" alt="" width="82" height="82" /></a>
      <nav>
        <a href="/">⌂　ΑΡΧΙΚΗ</a>
        <a href="/about">◇　ΣΥΛΛΟΓΟΣ</a>
        <a href="/team">♙　ΟΜΑΔΑ</a>
        <a href="/academy">⚑　ΑΚΑΔΗΜΙΑ</a>
        <a href="/team?tab=schedule">▦　ΑΓΩΝΕΣ</a>
        <a href="/news">▤　ΝΕΑ</a>
        <a href="/shop">▣　ΚΑΤΑΣΤΗΜΑ</a>
        <a href="/contact">☎　ΕΠΙΚΟΙΝΩΝΙΑ</a>
      </nav>
      <p>EST. <strong>2024</strong></p>
    </aside>
    <div class="login-core">
      <header class="login-topbar">
        <a href="/" class="login-topbar-brand"><img src="/mobile/club-logo.png" alt="" width="38" height="38" />ΛΕΥΤΕΡΙΑ <em>FC</em></a>
        <a class="login-topbar-home" href="/"><MobileIcon name="back" size={16} /> ΑΡΧΙΚΗ ΣΕΛΙΔΑ</a>
      </header>
      <div class="login-content">
    <div class="login-welcome" style:background-image={`linear-gradient(180deg,#080a0bc9,#080a0b18 45%,#080a0b),url("${publicImages.hero}")`}>
      <a class="login-home" href="/">← ΛΕΥΤΕΡΙΑ FC</a>
      <p class="welcome-kicker">ΛΕΥΤΕΡΙΑ FC</p>
      <h2>ΚΑΛΩΣ ΗΡΘΑΤΕ ΣΤΗ<br /><em>ΛΕΥΤΕΡΙΑ FC.</em></h2>
      <p>Η ψηφιακή κοινότητα της οικογένειας της Λευτεριάς FC. Ποδοσφαιριστές, γονείς και φίλαθλοι, όλοι μαζί, πιο κοντά στην ομάδα μας.</p>
    </div>
    <div class="login-form-panel">
    <header class="brand">
      <img src="/mobile/club-logo.png" alt="" width="56" height="56" />
      <div><h1>ΣΥΝΔΕΣΗ ΣΤΗΝ ΕΦΑΡΜΟΓΗ</h1>
      <p>Επίλεξε τον ρόλο σου και συνδέσου στον λογαριασμό σου.</p></div>
    </header>

    <div class="login-card">
      {#if error}<p class="error" role="alert" id={`${id}-error`} data-testid="login-error">{error}</p>{/if}

      {#if step === 'phone'}
        <div class="login-audience-grid" role="group" aria-label="Προβολή ρόλου">
          <button type="button" class:chosen={selectedAudience==='player'}
            aria-pressed={selectedAudience==='player'} onclick={()=>selectedAudience='player'} disabled={isBusy}
            style:background-image={`linear-gradient(0deg,#080909f5,#08090926),url("${publicImages.hero}")`}
            data-testid="member-role-player">ΠΟΔΟΣΦΑΙΡΙΣΤΗΣ</button>
          <button type="button" class:chosen={selectedAudience==='parent'}
            aria-pressed={selectedAudience==='parent'} onclick={()=>selectedAudience='parent'} disabled={isBusy}
            style:background-image={`linear-gradient(0deg,#080909f5,#08090926),url("${publicImages.academy}")`}
            data-testid="member-role-parent">ΓΟΝΕΑΣ</button>
          <button type="button" class:chosen={selectedAudience==='fan'}
            aria-pressed={selectedAudience==='fan'} onclick={()=>selectedAudience='fan'} disabled={isBusy}
            style:background-image={`linear-gradient(0deg,#080909f5,#08090926),url("${publicImages.supporter}")`}
            data-testid="member-role-fan">ΦΙΛΑΘΛΟΣ</button>
        </div>
        <p class="login-audience-note">Η ταυτότητα και τα δικαιώματα πρόσβασης επιβεβαιώνονται από το σύστημα μετά τον κωδικό SMS.</p>
        <form onsubmit={(event) => { event.preventDefault(); void run(() => onRequestOtp(phone.trim())); }}
          aria-describedby={error ? `${id}-error` : undefined} data-testid="phone-step">
          <fieldset disabled={isBusy}>
            <label for={`${id}-phone`}>Αριθμός τηλεφώνου</label>
            <div class="phone-field">
              <MobileIcon name="phone" size={19} />
              <input bind:this={phoneInput} id={`${id}-phone`} type="tel" autocomplete="tel" bind:value={phone}
                placeholder="+357 99 123456" required maxlength="40" aria-describedby={`${id}-phone-help`} data-testid="phone-input" />
            </div>
            <p class="hint" id={`${id}-phone-help`}>Εισάγετε τον αριθμό που είναι καταχωρημένος στη Λευτεριά FC.</p>
            {@render rememberChoice()}
            <button class="primary" type="submit" data-testid="request-otp-btn">
              {isBusy ? 'Αποστολή…' : 'Αποστολή κωδικού'}<MobileIcon name="arrow" size={20} />
            </button>
          </fieldset>
          <div class="signup-prompt">
            <p>Δεν είσαι μέλος της Λευτεριά FC;</p>
            <button type="button" class="text-button gold" disabled={isBusy} onclick={() => changeStep('fan_register')} data-testid="fan-register-link">Εγγράψου τώρα →</button>
          </div>
        </form>
      {:else if step === 'fan_register'}
        <form onsubmit={(event) => { event.preventDefault(); void run(() => onRegisterFan({ firstName: firstName.trim(), lastName: lastName.trim(), phone: phone.trim(), birthday })); }} data-testid="fan-register-step">
          <h2>Γίνε φίλος της Λευτεριάς</h2>
          <p class="intro">Νέα, αγώνες, προβλέψεις και περισσότερα.</p>
          <fieldset disabled={isBusy}>
            <label for={`${id}-first-name`}>Όνομα</label>
            <input bind:this={firstNameInput} id={`${id}-first-name`} autocomplete="given-name" bind:value={firstName} required maxlength="100" data-testid="fan-first-name-input" />
            <label for={`${id}-last-name`}>Επώνυμο</label>
            <input id={`${id}-last-name`} autocomplete="family-name" bind:value={lastName} required maxlength="100" data-testid="fan-last-name-input" />
            <label for={`${id}-fan-phone`}>Τηλέφωνο</label>
            <input id={`${id}-fan-phone`} type="tel" autocomplete="tel" bind:value={phone} placeholder="+357 99 123456" required maxlength="40" data-testid="fan-phone-input" />
            <label for={`${id}-birthday`}>Ημερομηνία γέννησης</label>
            <input id={`${id}-birthday`} type="date" autocomplete="bday" bind:value={birthday} required data-testid="fan-birthday-input" />
            <button class="primary" type="submit" data-testid="fan-register-submit">{isBusy ? 'Εγγραφή…' : 'Εγγραφή'}<MobileIcon name="arrow" size={20} /></button>
          </fieldset>
          <button type="button" class="text-button back" disabled={isBusy} onclick={() => changeStep('phone')} data-testid="fan-back-btn"><MobileIcon name="back" size={17} />Πίσω στη σύνδεση</button>
        </form>
      {:else if step === 'otp'}
        <form onsubmit={(event) => { event.preventDefault(); void run(() => onVerifyOtp(code, rememberDays)); }} data-testid="otp-step">
          <div class="step-icon"><MobileIcon name="shield" size={25} /></div>
          <h2>Εισάγετε τον κωδικό</h2>
          <p class="intro">Στάλθηκε στο <strong>{phone}</strong></p>
          {#if detectedRole}<span class="role-pill">{ROLE_NAMES[detectedRole]}</span>{/if}
          <fieldset disabled={isBusy}>
            <label class="code-label" for={`${id}-code`}>Εξαψήφιος κωδικός SMS</label>
            <input bind:this={otpInput} id={`${id}-code`} class="otp-code" type="text" inputmode="numeric" autocomplete="one-time-code"
              value={code} oninput={(event) => { code = event.currentTarget.value.replace(/[^0-9]/g, '').slice(0, 6); event.currentTarget.value = code; }}
              pattern={'[0-9]{6}'} minlength="6" maxlength="6" required data-testid="otp-input" />
            {@render rememberChoice()}
            <button class="primary" type="submit" data-testid="verify-otp-btn">{isBusy ? 'Επαλήθευση…' : 'Σύνδεση'}<MobileIcon name="arrow" size={20} /></button>
          </fieldset>
          <div class="otp-actions">
            <button type="button" class="text-button" disabled={isBusy} onclick={() => changeStep('phone')} data-testid="back-to-phone">Αλλαγή αριθμού</button>
            {#if resendSeconds > 0}<span aria-live="polite">Νέος κωδικός σε {resendSeconds}s</span>
            {:else}<button type="button" class="text-button gold" disabled={isBusy} onclick={() => { code = ''; void run(() => onRequestOtp(phone.trim())); }} data-testid="resend-otp">Αποστολή ξανά</button>{/if}
          </div>
        </form>
      {:else}
        <div data-testid="choose-role-step">
          <div class="step-icon"><MobileIcon name="shield" size={25} /></div>
          <h2 bind:this={roleHeading} tabindex="-1">Πώς θέλεις να συνδεθείς;</h2>
          <p class="intro">Επίλεξε έναν από τους διαθέσιμους ρόλους σου.</p>
          {#if returnTab && returnTab !== 'home'}
            <p class="return-intent" data-testid="role-return-intent">Ζητούμενη σελίδα: <strong>{TAB_NAMES[returnTab]}</strong>. Επίλεξε ρόλο με πρόσβαση ή συνέχισε στην αρχική του άλλου ρόλου.</p>
          {/if}
          {@render rememberChoice()}
          <div class="identity-options">
            {#each identities as identity (identity.role)}
              <button class="identity-option" type="button" disabled={isBusy} onclick={() => void run(() => onChooseRole(identity.role, rememberDays))} data-testid={`choose-role-${identity.role}`}>
                <span><strong>{ROLE_NAMES[identity.role]}</strong><small>{identity.name}</small>
                  {#if returnTab && returnTab !== 'home'}<small class="intent-role-hint" data-testid={`role-intent-${identity.role}`}>{roleCanResumeTab(returnTab, identity.role) ? 'Πρόσβαση στη ζητούμενη σελίδα' : 'Μετάβαση στην αρχική του ρόλου'}</small>{/if}
                </span><MobileIcon name="arrow" size={19} />
              </button>
            {/each}
          </div>
          <button type="button" class="text-button back" disabled={isBusy} onclick={() => changeStep('otp')} data-testid="choose-role-back"><MobileIcon name="back" size={17} />Πίσω</button>
        </div>
      {/if}
    </div>
    </div>
      </div>
      <div class="login-benefits">
        <span>▦　Παρουσίες και πρόγραμμα</span>
        <span>♧　Ανακοινώσεις</span>
        <span>♕　Επόμενος αγώνας</span>
        <span>▤　Νέα Συλλόγου</span>
      </div>
    </div>
  </div>
  <footer>ΛΕΥΤΕΡΙΑ FC © {new Date().getFullYear()}</footer>
</section>

<style>
  .login-page { min-height:100dvh; display:flex; flex-direction:column; padding:env(safe-area-inset-top,0px) 24px env(safe-area-inset-bottom,0px); background:radial-gradient(ellipse at 15% 0%,#f6bb3812,transparent 42%),radial-gradient(ellipse at 100% 100%,#174b3020,transparent 45%),var(--lf-bg); }
  .login-content { width:100%; max-width:380px; flex:1; align-content:center; margin:0 auto; padding:40px 0 28px; }
  .brand { text-align:center; margin-bottom:32px; }
  .brand img { display:block; object-fit:contain; margin:0 auto 16px; filter:drop-shadow(0 7px 20px #f6bb3828); }
  .brand h1 { margin:0; font-size:36px; line-height:1.1; letter-spacing:.045em; }
  .brand p { margin:10px 0 0; color:var(--lf-gold); font-size:11px; font-weight:600; text-transform:uppercase; letter-spacing:.24em; }
  .brand p span { color:#626262; padding:0 6px; }
  .login-card { width:100%; }
  h2 { margin:0 0 6px; text-align:center; font-size:25px; line-height:1.2; }
  .intro { margin:0 0 22px; text-align:center; color:var(--lf-muted); font-size:13px; }
  .intro strong { color:#fff; font-weight:500; }
  fieldset { min-width:0; margin:0; padding:0; border:0; }
  label { display:block; margin:17px 0 7px; color:#c3c5c7; font-size:13px; }
  input,select { width:100%; min-height:49px; padding:12px 14px; border:1px solid var(--lf-line); border-radius:8px; background:#151718; color:#fff; }
  input::placeholder { color:#696e72; }
  .phone-field { position:relative; }
  .phone-field :global(svg) { position:absolute; top:15px; left:14px; color:#8f9498; pointer-events:none; }
  .phone-field input { padding-left:43px; font-size:18px; }
  .hint,.remember-choice p { margin:7px 0 0; color:#8b9094; font-size:11px; line-height:1.5; }
  .remember-choice { margin:21px 0; }
  .remember-choice label { margin-top:0; }
  .remember-choice select { min-height:44px; padding:9px 12px; font-size:13px; }
  button { cursor:pointer; }
  .primary { display:flex; width:100%; min-height:49px; align-items:center; justify-content:center; gap:10px; margin-top:22px; border:1px solid var(--lf-gold); border-radius:8px; background:linear-gradient(110deg,#ffcd47,#f6bb38); color:#090909; font-size:15px; font-weight:700; }
  .primary:hover:not(:disabled) { background:var(--lf-gold-light); }
  .text-button { min-height:44px; padding:8px 0; border:0; background:transparent; color:#b0b4b6; font-size:13px; }
  .text-button:hover:not(:disabled) { color:#fff; }
  .text-button.gold { color:var(--lf-gold); }
  .signup-prompt { margin-top:25px; padding-top:18px; border-top:1px solid #202223; text-align:center; }
  .signup-prompt p { margin:0; color:#92979a; font-size:12px; }
  .signup-prompt button { font-weight:600; }
  .error { margin:0 0 20px; padding:12px 14px; border:1px solid #ef444440; border-radius:8px; background:#ef444411; color:#fca5a5; font-size:13px; line-height:1.5; overflow-wrap:anywhere; }
  .step-icon { display:grid; width:52px; height:52px; place-items:center; margin:0 auto 16px; border-radius:50%; background:#f6bb3814; color:var(--lf-gold); }
  .role-pill { display:block; width:fit-content; margin:-9px auto 22px; padding:4px 12px; border-radius:20px; background:#f6bb3814; color:var(--lf-gold); font-size:12px; }
  .code-label { text-align:center; margin-top:22px; }
  .otp-code { height:60px; text-align:center; padding:8px 0 8px .48em; font-family:var(--lf-heading); font-size:32px; letter-spacing:.48em; }
  .otp-actions { display:flex; gap:12px; align-items:center; justify-content:space-between; margin-top:10px; }
  .otp-actions span { color:#8b9094; font-size:12px; }
  .back { display:flex; align-items:center; justify-content:center; gap:7px; width:100%; margin-top:10px; }
  .identity-options { display:grid; gap:10px; }
  .return-intent { color:var(--lf-muted); font-size:13px; line-height:1.5; margin:0 0 12px; }
  .identity-option .intent-role-hint { color:var(--lf-gold-light); font-size:12px; }
  .identity-option { display:flex; align-items:center; justify-content:space-between; gap:12px; width:100%; min-height:76px; padding:14px 16px; border:1px solid var(--lf-line); border-radius:8px; background:linear-gradient(120deg,#151819,#101112); color:var(--lf-gold); text-align:left; }
  .identity-option:hover:not(:disabled) { border-color:var(--lf-gold); background:#1a1c1d; }
  .identity-option strong { display:block; color:#fff; font-size:14px; font-weight:600; }
  .identity-option small { display:block; margin-top:3px; color:var(--lf-muted); font-size:12px; }
  footer { padding:18px 0 22px; text-align:center; color:#686d70; font-size:11px; }
  @media (max-width:360px) { .login-page{padding-right:18px;padding-left:18px}.brand img{width:96px;height:96px}.brand{margin-bottom:24px}.login-content{padding-top:28px} }
  @media (prefers-reduced-motion:reduce) { *,*::before,*::after { transition:none!important; } }

  /* Presentation only: all OTP, registration and available-role steps above remain intact. */
  .login-page { background:radial-gradient(ellipse at top right,#f6bb3814,transparent 55%),#080a0b; }
  .login-content { max-width:1220px; display:grid; grid-template-columns:minmax(0,1.15fr) minmax(0,1fr); gap:clamp(24px,4vw,64px); align-items:center; }
  .login-welcome { min-height:640px; padding:36px; display:flex; flex-direction:column; justify-content:flex-end; background-size:cover; background-position:center; border:1px solid #77705a; border-radius:12px; }
  .login-home { align-self:flex-start; margin-bottom:auto; min-height:44px; color:#fff; text-decoration:none; font-weight:700; }
  .welcome-kicker { color:var(--lf-gold); font-weight:700; font-size:14px; }
  .login-welcome h2 { text-align:left; font-size:clamp(36px,4vw,58px); font-style:italic; line-height:1.02; margin:12px 0; }
  .login-welcome em { color:var(--lf-gold); }
  .login-welcome>p:last-child { max-width:38ch; font-size:15px; line-height:1.6; }
  .login-form-panel { min-width:0; padding:clamp(22px,3vw,38px); border:1px solid #8f8060; border-radius:12px; background:linear-gradient(145deg,#1b1c1c,#080a0b 70%); box-shadow:0 16px 45px #0006,inset 0 -2px #f6bb3860; }
  .brand { padding-bottom:24px; border-bottom:1px solid #f6bb3855; }
  .brand img { width:88px; height:88px; }
  .brand h1 { font-style:italic; }
  .primary { min-height:52px; box-shadow:0 3px 18px #f6bb381c; font-family:var(--lf-heading); font-size:20px; }
  .identity-option { border-color:#8d7850; background:linear-gradient(130deg,#302713,#111314 70%); }
  .identity-option strong { font-family:var(--lf-heading); font-size:21px; }
  @media(max-width:760px) {
    .login-content { display:block; max-width:520px; padding-top:20px; }
    .login-welcome { min-height:260px; padding:22px; margin-bottom:20px; background-position:center 35%; }
    .login-welcome h2 { font-size:34px; }
    .login-welcome>p:last-child { font-size:13px; }
    .login-home { margin-bottom:30px; }
    .login-form-panel { padding:22px; }
    .brand img { width:64px; height:64px; }
    .brand h1 { font-size:30px; }
  }

  /* Club reference: photographic welcome, choice cards and polished access
     panel. The SMS OTP/role verification handlers above are unchanged. */
  .login-page{padding:0;background:#080909}
  .login-platform{display:grid;grid-template-columns:clamp(110px,9vw,154px) minmax(0,1fr);flex:1;min-width:0}
  .login-sidebar{background:#090a0b;border-right:1px solid #383735;display:flex;flex-direction:column;align-items:center;padding:24px 0;gap:24px}
  .login-sidebar>a img{width:86px;height:86px;object-fit:contain}
  .login-sidebar nav{display:grid;width:100%;gap:2px}
  .login-sidebar nav a{color:#eee;text-decoration:none;min-height:46px;display:flex;align-items:center;padding:9px 12px;font-family:var(--lf-heading);font-weight:800;font-size:13px}
  .login-sidebar nav a:hover,.login-sidebar nav a:focus-visible{color:#ffca46;background:#f6bb3814}
  .login-sidebar>p{margin:auto 0 0;color:#aaa;font-size:11px}.login-sidebar>p strong{color:#fff;font-size:20px}
  .login-core{min-width:0;display:flex;flex-direction:column}
  .login-topbar{display:flex;align-items:center;justify-content:space-between;min-height:62px;padding:8px clamp(20px,3vw,46px);border-bottom:2px solid #e3a523;background:#090a0b}
  .login-topbar-brand{display:flex;align-items:center;gap:10px;color:#fff;text-decoration:none;font:italic 800 24px/1 var(--lf-heading)}
  .login-topbar-brand img{object-fit:contain}.login-topbar-brand em{color:var(--lf-gold)}
  .login-topbar-home{color:#ffca46;text-decoration:none;display:flex;align-items:center;gap:8px;font-weight:700}
  .login-content{width:100%;max-width:none;display:grid;grid-template-columns:minmax(0,1fr) minmax(440px,1fr);gap:clamp(14px,2.6vw,36px);padding:clamp(15px,2vw,30px);align-items:stretch;min-height:calc(100dvh - 180px)}
  .login-welcome{min-height:0;border:0;border-radius:0;justify-content:flex-start;gap:16px;padding:clamp(26px,4vw,62px);background-position:center;background-size:cover}
  .login-home{margin-bottom:clamp(15px,3vw,35px)}
  .welcome-kicker{font:italic 800 clamp(17px,1.6vw,24px)/1.12 var(--lf-heading)}
  .login-welcome h2{text-align:left;font:italic 900 clamp(34px,3.7vw,56px)/1.04 var(--lf-heading);margin:0}
  .login-welcome>p:last-child{font-size:clamp(15px,1.2vw,19px);line-height:1.45;max-width:44ch}
  .login-form-panel{border:1px solid #b6a78b;border-radius:15px;padding:clamp(22px,2.8vw,44px);background:linear-gradient(155deg,#16191be8,#060708f6 78%);box-shadow:0 12px 45px #000a,inset 0 -1px #f6bb3870}
  .brand{display:flex;justify-content:center;align-items:center;gap:14px;padding-bottom:10px;margin-bottom:13px;border:0}
  .brand img{width:56px;height:56px;margin:0}
  .brand h1{text-align:left;font:italic 800 clamp(23px,1.85vw,32px)/1.06 var(--lf-heading)}
  .brand p{text-align:left;letter-spacing:0;text-transform:none;margin:8px 0 0;font-size:12px;color:#dfdede}
  .login-audience-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px;margin:14px 0 8px}
  .login-audience-grid button{position:relative;display:flex;justify-content:center;align-items:flex-end;min-height:clamp(98px,9vw,145px);padding:12px 5px 15px;background-color:#151515;background-size:cover;background-position:center;color:#fff;font:800 clamp(14px,1.1vw,18px)/1.2 var(--lf-heading);border:1px solid #7d7d7d;border-radius:10px;box-shadow:inset 0 -30px 30px #000a}
  .login-audience-grid button.chosen{border:2px solid var(--lf-gold);box-shadow:0 0 0 1px #f6bb3855,0 0 18px #f6bb3838,inset 0 -30px 30px #000a}
  .login-audience-grid button.chosen::after{content:"✓";position:absolute;right:5px;top:5px;width:23px;height:23px;border-radius:50%;background:var(--lf-gold);color:#060606;display:grid;place-items:center;font:800 15px var(--lf-body)}
  .login-audience-note{color:#9b9e9e;font-size:11px;line-height:1.35;margin:0 0 13px}
  .login-form-panel label{margin:11px 0 6px}
  .login-form-panel .remember-choice{margin:11px 0}
  .login-form-panel .remember-choice p{margin-top:4px}
  .login-form-panel .primary{margin-top:14px;min-height:49px;border-radius:6px}
  .login-form-panel .signup-prompt{margin-top:12px;padding-top:10px}
  .login-benefits{display:flex;flex-wrap:wrap;justify-content:space-around;gap:10px;padding:18px 20px;background:#101213;border-top:1px solid #5b5140;color:#dedede;font:700 15px var(--lf-heading)}
  .login-page>footer{padding:8px;color:#898989;background:#080909}
  @media(max-width:1100px){.login-content{grid-template-columns:minmax(0,1fr);max-width:700px;margin:auto}.login-welcome{min-height:275px}.login-form-panel{padding:22px}.login-platform{grid-template-columns:92px minmax(0,1fr)}.login-sidebar nav a{font-size:11px;padding-inline:6px}.login-audience-grid button{min-height:100px}}
  @media(max-width:720px){.login-platform{display:block}.login-sidebar{display:none}.login-topbar{padding:8px 16px}.login-topbar-brand{font-size:22px}.login-topbar-home{font-size:12px}.login-content{padding:15px;max-width:560px}.login-welcome{min-height:230px;padding:22px;background-position:center 42%;margin:0}.login-welcome h2{font-size:31px}.login-home{margin:0}.brand h1{font-size:26px}.login-benefits{font-size:12px;justify-content:flex-start}}
  @media(max-width:430px){.login-audience-grid{gap:6px}.login-audience-grid button{font-size:13px;min-height:90px;padding-inline:3px}.login-form-panel{padding:16px}.brand img{width:42px;height:42px}.brand h1{font-size:23px}}

</style>
