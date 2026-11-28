<script lang="ts">
  import {getContext} from 'svelte';
  import {browser} from '$app/env';
  import {goto} from '$app/navigation';
  import {ADMIN_AUTH_CONTEXT,type AdminAuthController} from '../../../lib/admin/controller.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import AdminProfilePhoto from '../../../lib/admin/AdminProfilePhoto.svelte';
  import {adminPhotoRevision} from '../../../lib/admin/photo-revision.ts';
  import {ADMIN_ROLE_NAMES,isAdminRole,adminCapabilityName} from '../../../lib/admin/permissions.ts';

  const auth=getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  $effect(()=>{if(browser&&$auth.status==='anonymous')void goto('/admin/login',{replace:true});});
  const user=$derived($auth.session?.user);
  const roleName=$derived(user&&isAdminRole(user.role)?ADMIN_ROLE_NAMES[user.role]:'—');
  const initials=$derived(user?(user.name||user.username).trim().split(/\s+/u)
    .slice(0,2).map(value=>value.slice(0,1).toLocaleUpperCase('el-GR')).join(''):'FC');

  let profileActor=$state('');
  let displayName=$state('');
  let namePassword=$state(''),currentPassword=$state(''),nextPassword=$state(''),confirmPassword=$state('');
  let savingName=$state(false),savingPassword=$state(false);
  let nameError=$state(''),nameSuccess=$state(''),passwordError=$state('');
  let emailValue=$state(''),emailPassword=$state('');
  let emailReading=$state(false),emailSaving=$state(false),emailRevision=$state(0);
  let emailError=$state(''),emailSuccess=$state('');
  let emailLoaded=$state(false);
  let photoBusy=$state(false),photoError=$state(''),photoSuccess=$state('');
  let photoInput:HTMLInputElement;
  async function uploadOwnPhoto(event:SubmitEvent){
    event.preventDefault();
    if(!browser||!user||photoBusy||$auth.status!=='authenticated')return;
    const actor=user.id,file=photoInput?.files?.[0];
    photoError='';photoSuccess='';
    if(!file||file.size===0||file.size>5*1024*1024||
      !['image/jpeg','image/png','image/gif','image/webp'].includes(file.type)){
      photoError='Επιλέξτε έγκυρη εικόνα JPG, PNG, GIF ή WebP έως 5 MB.';return;
    }
    const form=new FormData();
    form.append('file',file,file.name);
    photoBusy=true;
    try{
      const response=await fetch('/api/admin/auth/profile/photo',{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'X-Lefteria-Admin':'1'},body:form
      });
      if(user?.id!==actor)return;
      if(response.status===401||response.status===403){
        auth.invalidateSession();throw Error('Η σύνδεσή σας άλλαξε. Συνδεθείτε ξανά.');
      }
      if(response.status!==204)throw Error(
        response.status===413?'Η εικόνα υπερβαίνει το επιτρεπόμενο μέγεθος.':
        'Η εικόνα δεν αποθηκεύτηκε. Δοκιμάστε άλλη φωτογραφία.');
      if(photoInput)photoInput.value='';
      adminPhotoRevision.update(value=>value+1);
      photoSuccess='Η φωτογραφία του λογαριασμού ενημερώθηκε.';
    }catch(e){
      if(user?.id===actor)photoError=e instanceof Error?e.message:'Η μεταφόρτωση απέτυχε.';
    }finally{if(user?.id===actor)photoBusy=false;}
  }
  async function removeOwnPhoto(){
    if(!browser||!user||photoBusy||$auth.status!=='authenticated')return;
    const actor=user.id;photoError='';photoSuccess='';photoBusy=true;
    try{
      const response=await fetch('/api/admin/auth/profile/photo',{
        method:'DELETE',credentials:'same-origin',cache:'no-store',
        headers:{'X-Lefteria-Admin':'1','Content-Type':'application/json'},
        body:'{}'
      });
      if(user?.id!==actor)return;
      if(response.status===401||response.status===403){
        auth.invalidateSession();throw Error('Η σύνδεσή σας άλλαξε. Συνδεθείτε ξανά.');
      }
      if(response.status!==204)throw Error('Η αφαίρεση φωτογραφίας δεν επιβεβαιώθηκε.');
      if(photoInput)photoInput.value='';
      adminPhotoRevision.update(value=>value+1);
      photoSuccess='Η φωτογραφία αφαιρέθηκε.';
    }catch(e){
      if(user?.id===actor)photoError=e instanceof Error?e.message:'Η αφαίρεση απέτυχε.';
    }finally{if(user?.id===actor)photoBusy=false;}
  }


  $effect(()=>{
    const actor=user?.id??'';
    if(actor!==profileActor){
      profileActor=actor;displayName=user?.name??'';
      namePassword='';currentPassword='';nextPassword='';confirmPassword='';
      nameError='';nameSuccess='';passwordError='';
      emailValue='';emailPassword='';emailError='';emailSuccess='';emailLoaded=false;
      photoError='';photoSuccess='';photoBusy=false;
      if(photoInput)photoInput.value='';
    }
  });

  $effect(()=>{
    const actor=user?.id,revision=emailRevision;
    void revision;
    emailLoaded=false;emailError='';emailValue='';
    if(!browser||!actor||$auth.status!=='authenticated')return;
    const controller=new AbortController();
    emailReading=true;
    void(async()=>{
      try{
        const response=await fetch('/api/admin/auth/profile/contact-email',{
          method:'GET',credentials:'same-origin',cache:'no-store',
          headers:{'Accept':'application/json'},signal:controller.signal
        });
        if(controller.signal.aborted||user?.id!==actor)return;
        if(response.status===401||response.status===403){
          auth.invalidateSession();return;
        }
        if(!response.ok)throw Error('Το email επικοινωνίας δεν είναι διαθέσιμο σε αυτή την εγκατάσταση.');
        const raw:unknown=await response.json();
        if(!raw||typeof raw!=='object'||Array.isArray(raw)||
          !('email' in raw)||!('verified' in raw)||
          raw.verified!==false||!(raw.email===null||
            typeof raw.email==='string'&&raw.email.length<=254))
          throw Error('Η απάντηση στοιχείων email δεν επαληθεύτηκε.');
        if(!controller.signal.aborted&&user?.id===actor){
          emailValue=raw.email??'';
          emailLoaded=true;
        }
      }catch(e){
        if(!controller.signal.aborted&&user?.id===actor)
          emailError=e instanceof Error?e.message:'Το email επικοινωνίας δεν ανακτήθηκε.';
      }finally{
        if(!controller.signal.aborted&&user?.id===actor)emailReading=false;
      }
    })();
    return()=>controller.abort();
  });
  async function saveContactEmail(event:SubmitEvent){
    event.preventDefault();
    if(!browser||!user||!emailLoaded||emailSaving||savingName||savingPassword)return;
    const actor=user.id,email=emailValue.trim().toLowerCase(),secret=emailPassword;
    if(!/^[a-z0-9._%+-]+@[a-z0-9.-]+\.[a-z]{2,63}$/.test(email)||
      email.length>254||email.includes('..')||
      !secret||secret.length>4096){
      emailError='Πληκτρολογήστε έγκυρο email και τον τρέχοντα κωδικό σας.';
      return;
    }
    emailSaving=true;emailError='';emailSuccess='';
    try{
      const response=await fetch('/api/admin/auth/profile/contact-email',{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','Accept':'application/json',
          'X-Lefteria-Admin':'1'},
        body:JSON.stringify({email,current_password:secret})
      });
      if(user?.id!==actor)return;
      if(response.status===401||response.status===403){
        auth.invalidateSession();throw Error('Η συνεδρία ή η πρόσβασή σας άλλαξε.');
      }
      if(response.status===409){auth.invalidateSession();throw Error('Ο λογαριασμός άλλαξε από άλλη συσκευή.');}
      if(!response.ok)throw Error('Δεν αποθηκεύτηκε το email επικοινωνίας.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw)||
        !('updated' in raw)||raw.updated!==true||
        !('email' in raw)||raw.email!==email||
        !('verified' in raw)||raw.verified!==false)
        throw Error('Η αποθήκευση του email δεν επαληθεύτηκε.');
      emailValue=email;
      emailSuccess='Το email επικοινωνίας αποθηκεύτηκε χωρίς επαλήθευση ιδιοκτησίας. Δεν χρησιμοποιείται για σύνδεση ή ανάκτηση κωδικού.';
      emailRevision++;
    }catch(e){if(user?.id===actor)emailError=e instanceof Error?e.message:'Η αλλαγή απέτυχε.';}
    finally{emailSaving=false;emailPassword='';}
  }
  async function saveName(event:SubmitEvent){
    event.preventDefault();
    if(!browser||savingName||savingPassword||!user)return;
    const actor=user.id,choice=displayName,secret=namePassword;
    if(choice!==choice.trim()||choice.length<1||choice.length>200||
      !secret||secret.length>4096){
      nameError='Ελέγξτε το όνομα και πληκτρολογήστε τον τρέχοντα κωδικό σας.';return;
    }
    nameError='';nameSuccess='';savingName=true;
    try{
      const response=await fetch('/api/admin/auth/profile/name',{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','Accept':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({name:choice,current_password:secret})
      });
      if(user.id!==actor)return;
      if(response.status===401||response.status===403){auth.invalidateSession();throw Error('Η σύνδεσή σας άλλαξε. Συνδεθείτε ξανά.');}
      if(response.status===409){void auth.bootstrap();throw Error('Τα στοιχεία άλλαξαν από άλλη συσκευή. Συνδεθείτε ξανά.');}
      if(!response.ok)throw Error('Δεν αποθηκεύτηκε το όνομα. Ελέγξτε τον τρέχοντα κωδικό.');
      const raw:unknown=await response.json();
      if(!raw||typeof raw!=='object'||Array.isArray(raw)||!('updated' in raw)||
        raw.updated!==true||!('name' in raw)||raw.name!==choice)
        throw Error('Δεν επιβεβαιώθηκε η ενημέρωση ονόματος.');
      namePassword='';
      nameSuccess='Το όνομά σας ενημερώθηκε και επαληθεύτηκε.';
      await auth.bootstrap();
    }catch(e){if(user?.id===actor)nameError=e instanceof Error?e.message:'Η αλλαγή απέτυχε.';}
    finally{savingName=false;namePassword='';}
  }
  async function savePassword(event:SubmitEvent){
    event.preventDefault();
    if(!browser||savingName||savingPassword||!user)return;
    const actor=user.id,old=currentPassword,next=nextPassword;
    if(next!==confirmPassword||next===old||next.length<12||next.length>72||!old){
      passwordError='Ο νέος κωδικός πρέπει να είναι διαφορετικός, από 12 έως 72 χαρακτήρες και να συμφωνεί με την επιβεβαίωση.';return;
    }
    passwordError='';savingPassword=true;
    try{
      const response=await fetch('/api/admin/auth/profile/password',{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({current_password:old,next_password:next})
      });
      if(user.id!==actor)return;
      if(response.status===401||response.status===403){auth.invalidateSession();throw Error('Η σύνδεσή σας άλλαξε. Συνδεθείτε ξανά.');}
      if(response.status===409){auth.invalidateSession();throw Error('Ο κωδικός άλλαξε από άλλη συσκευή. Συνδεθείτε ξανά.');}
      if(response.status!==204)throw Error('Η αλλαγή κωδικού δεν επιβεβαιώθηκε. Ελέγξτε τον τρέχοντα κωδικό.');
      currentPassword='';nextPassword='';confirmPassword='';
      auth.invalidateSession();
      await goto('/admin/login',{replace:true});
    }catch(e){if(user?.id===actor)passwordError=e instanceof Error?e.message:'Η αλλαγή κωδικού απέτυχε.';}
    finally{savingPassword=false;currentPassword='';nextPassword='';confirmPassword='';}
  }
  const expiry=$derived($auth.session?
    new Intl.DateTimeFormat('el-GR',{dateStyle:'medium',timeStyle:'short',timeZone:'Asia/Nicosia'})
      .format(new Date($auth.session.expires_at)):'');
</script>
<svelte:head><title>Το προφίλ μου | Λευτεριά FC OS</title></svelte:head>
{#if $auth.status==='authenticated' && $auth.session && user}
  <AdminFrame {user} active="profile" busy={$auth.busy} onLogout={()=>void auth.logout()}>
    <div class="admin-my-profile" data-testid="admin-my-profile">
      <header class="admin-profile-page-header">
        <div><p class="admin-eyebrow">ΠΡΟΣΩΠΙΚΟΣ ΛΟΓΑΡΙΑΣΜΟΣ</p>
          <h1>Το προφίλ μου</h1>
          <p class="admin-muted">Τα στοιχεία αυτά ανήκουν στον συνδεδεμένο, επαληθευμένο λογαριασμό διαχείρισης.</p>
        </div>
        <button class="admin-secondary" type="button" disabled={$auth.busy}
          onclick={()=>void auth.bootstrap()}>Ανανέωση στοιχείων</button>
      </header>
      <section class="admin-panel admin-profile-identity" aria-label="Προσωπικά στοιχεία">
        <AdminProfilePhoto actorId={user.id} {initials} size="large"/>
        <div><h2>{user.name || user.username}</h2>
          <p class="admin-muted">{roleName} · {user.username}</p>
          <p class="admin-muted">Η φωτογραφία είναι ιδιωτική και εμφανίζεται μόνο στον δικό σας λογαριασμό.</p>
        </div>
      </section>
      <div class="admin-profile-columns">
        <section class="admin-panel" id="account" aria-labelledby="my-profile-account-heading">
          <h2 id="my-profile-account-heading">Στοιχεία λογαριασμού</h2>
          <dl class="admin-details">
            <div><dt>Όνομα</dt><dd>{user.name || '—'}</dd></div>
            <div><dt>Όνομα χρήστη</dt><dd>{user.username}</dd></div>
            <div><dt>Ρόλος</dt><dd data-testid="admin-role">{roleName}</dd></div>
            <div><dt>Email επικοινωνίας</dt><dd>{emailLoaded ? (emailValue || 'Δεν έχει οριστεί') : 'Δεν έχει ανακτηθεί'} · Μη επαληθευμένο</dd></div>
          </dl>
          <form class="admin-self-form" onsubmit={saveName} data-testid="admin-self-name-form">
            <h3>Αλλαγή ονόματος</h3>
            <label for="admin-my-display-name">Όνομα εμφάνισης</label>
            <input id="admin-my-display-name" type="text" bind:value={displayName} required
              minlength="1" maxlength="200" autocomplete="name" disabled={savingName||savingPassword}/>
            <label for="admin-name-current-password">Τρέχων κωδικός για επιβεβαίωση</label>
            <input id="admin-name-current-password" type="password" bind:value={namePassword}
              required autocomplete="current-password" disabled={savingName||savingPassword}/>
            <button type="submit" class="admin-primary" disabled={savingName||savingPassword}>
              {savingName?'Αποθήκευση…':'ΑΠΟΘΗΚΕΥΣΗ ΟΝΟΜΑΤΟΣ'}
            </button>
            {#if nameError}<p role="alert" class="admin-self-error">{nameError}</p>{/if}
            {#if nameSuccess}<p role="status" class="admin-self-success">{nameSuccess}</p>{/if}
          </form>
          <form class="admin-self-form" onsubmit={saveContactEmail} data-testid="admin-self-contact-email-form">
            <h3>Email επικοινωνίας</h3>
            <p class="admin-profile-unavailable">Η αλλαγή email απαιτεί επαλήθευση της νέας διεύθυνσης πριν
              χρησιμοποιηθεί για ειδοποιήσεις, προσκλήσεις ή ανάκτηση κωδικού.
              Προς το παρόν αποθηκεύεται μόνο ως <strong>μη επαληθευμένο στοιχείο προφίλ</strong>.</p>
            <label for="admin-own-contact-email">Νέα διεύθυνση email</label>
            <input type="email" id="admin-own-contact-email" autocomplete="email" required maxlength="254"
              bind:value={emailValue} disabled={!emailLoaded||emailReading||emailSaving||savingName||savingPassword}
              placeholder="you@example.com"/>
            <label for="admin-own-contact-secret">Τρέχων κωδικός για επιβεβαίωση</label>
            <input type="password" id="admin-own-contact-secret" autocomplete="current-password"
              required bind:value={emailPassword} disabled={!emailLoaded||emailSaving||savingName||savingPassword}/>
            <button class="admin-primary" type="submit"
              disabled={!emailLoaded||emailReading||emailSaving||savingName||savingPassword}>
              {emailSaving ? 'Αποθήκευση…' : 'ΑΠΟΘΗΚΕΥΣΗ EMAIL ΕΠΙΚΟΙΝΩΝΙΑΣ'}
            </button>
            {#if emailReading}<p role="status">Φόρτωση email…</p>{/if}
            {#if emailError}<p role="alert" class="admin-self-error">{emailError}</p>{/if}
            {#if emailSuccess}<p role="status" class="admin-self-success">{emailSuccess}</p>{/if}
          </form>
          <form class="admin-self-form" onsubmit={uploadOwnPhoto} data-testid="admin-self-photo-form">
            <h3>Φωτογραφία προφίλ</h3>
            <p class="admin-muted">Η εικόνα μετατρέπεται σε προστατευμένο JPEG, χωρίς μεταδεδομένα τοποθεσίας. Έως 5 MB.</p>
            <label for="admin-own-photo-file">Επιλογή προσωπικής φωτογραφίας</label>
            <input id="admin-own-photo-file" type="file" accept="image/jpeg,image/png,image/gif,image/webp"
              bind:this={photoInput} disabled={photoBusy||$auth.busy}/>
            <button type="submit" class="admin-primary" disabled={photoBusy||$auth.busy}>
              {photoBusy?'Επεξεργασία…':'ΑΠΟΘΗΚΕΥΣΗ ΦΩΤΟΓΡΑΦΙΑΣ'}
            </button>
            <button type="button" class="admin-secondary" disabled={photoBusy||$auth.busy}
              onclick={()=>void removeOwnPhoto()}>ΑΦΑΙΡΕΣΗ ΦΩΤΟΓΡΑΦΙΑΣ</button>
            {#if photoError}<p role="alert" class="admin-self-error">{photoError}</p>{/if}
            {#if photoSuccess}<p role="status" class="admin-self-success">{photoSuccess}</p>{/if}
          </form>
        </section>
        <section class="admin-panel" id="security" aria-labelledby="my-profile-security-heading">
          <h2 id="my-profile-security-heading">Ασφάλεια & σύνδεση</h2>
          <p>Η τρέχουσα σύνδεση λήγει <time datetime={$auth.session.expires_at}>{expiry}</time> (ώρα Κύπρου).</p>
          <p class="admin-muted">Η αλλαγή κωδικού απαιτεί τον τρέχοντα κωδικό.
            Μετά την επιβεβαίωση αποσυνδέονται όλες οι συσκευές.</p>
          <form class="admin-self-form" onsubmit={savePassword} data-testid="admin-self-password-form">
            <label for="admin-password-current">Τρέχων κωδικός</label>
            <input type="password" id="admin-password-current" required autocomplete="current-password"
              bind:value={currentPassword} disabled={savingPassword||savingName}/>
            <label for="admin-password-next">Νέος κωδικός</label>
            <input type="password" id="admin-password-next" required minlength="12" maxlength="72"
              autocomplete="new-password" bind:value={nextPassword} disabled={savingPassword||savingName}/>
            <label for="admin-password-confirm">Επιβεβαίωση νέου κωδικού</label>
            <input type="password" id="admin-password-confirm" required minlength="12" maxlength="72"
              autocomplete="new-password" bind:value={confirmPassword} disabled={savingPassword||savingName}/>
            <button type="submit" class="admin-primary" disabled={savingPassword||savingName}>
              {savingPassword?'Ενημέρωση…':'ΑΛΛΑΓΗ ΚΩΔΙΚΟΥ'}
            </button>
            {#if passwordError}<p role="alert" class="admin-self-error">{passwordError}</p>{/if}
          </form>
          <button class="admin-secondary" type="button" disabled={$auth.busy}
            onclick={()=>void auth.logout()}>Αποσύνδεση από αυτή τη συσκευή</button>
        </section>
      </div>
      <section class="admin-panel" id="access" aria-labelledby="my-profile-access-heading">
        <h2 id="my-profile-access-heading">Οι αναθέσεις μου</h2>
        {#if user.organizations.length}
          <ul class="admin-profile-orgs">
            {#each user.organizations as org (org.id)}
              <li><strong>{org.name}</strong><span>Επαληθευμένη πρόσβαση</span></li>
            {/each}
          </ul>
          <details class="admin-profile-permissions">
            <summary>Προβολή επιμέρους δικαιωμάτων</summary>
            <ul>{#each user.permissions as permission (permission)}
              <li>{adminCapabilityName(permission)}</li>
            {/each}</ul>
          </details>
        {:else}
          <p class="admin-muted">Δεν έχει ανατεθεί οργανισμός.</p>
        {/if}
      </section>
    </div>
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status==='unavailable'} busy={$auth.busy}
    error={$auth.error} onRetry={()=>void auth.bootstrap()} onLogout={()=>void auth.logout()}/>
{/if}
<style>
  .admin-my-profile{display:grid;gap:16px;width:100%;max-width:none;min-width:0}
  .admin-profile-page-header{display:flex;align-items:flex-end;justify-content:space-between;flex-wrap:wrap;gap:20px}
  .admin-profile-page-header h1{margin:6px 0 10px}
  .admin-profile-identity{display:flex;align-items:center;gap:20px}
  .admin-profile-initials{display:grid;place-items:center;width:96px;height:96px;flex:none;border-radius:50%;
    background:linear-gradient(140deg,#ffce5b,#9e6c13);color:#171109;
    font:900 37px/1 'Fira Sans Extra Condensed',sans-serif}
  .admin-profile-identity h2{margin:0 0 8px}
  .admin-profile-columns{display:grid;grid-template-columns:1fr 1fr;gap:16px}
  .admin-profile-columns .admin-panel{margin:0}
  .admin-profile-unavailable{font-size:13px;line-height:1.55;color:#dbbf8b;margin-top:17px}
  .admin-profile-orgs{display:flex;gap:12px;flex-wrap:wrap;list-style:none;padding:0;margin:15px 0}
  .admin-profile-orgs li{display:grid;gap:4px;background:#171a1b;border:1px solid #5f5130;padding:14px}
  .admin-profile-orgs span{color:#b2b8b9;font-size:12px}
  .admin-profile-permissions{margin-top:12px;color:#ddd}
  .admin-profile-permissions summary{cursor:pointer;color:#ffcb59}
  @media(max-width:750px){.admin-profile-columns{grid-template-columns:1fr}.admin-profile-identity{align-items:flex-start}}

  .admin-self-form{display:flex;flex-direction:column;gap:9px;
    margin-top:18px;padding-top:16px;border-top:1px solid #555}
  .admin-self-form h3{font-size:21px;margin:0 0 4px}
  .admin-self-form label{font-size:13px;font-weight:700;color:#e8ddc4}
  .admin-self-form :is(input){width:100%;min-width:0;min-height:45px;background:#0a0b0c;
    border:1px solid #777;color:#fff;padding:10px 12px;border-radius:5px}
  .admin-self-form input:focus-visible{outline:3px solid #f6bb38;outline-offset:2px}
  .admin-self-form .admin-primary{min-height:46px;margin-top:6px}
  .admin-self-error{color:#ffac9a;font-size:13px}
  .admin-self-success{color:#b8f0bf;font-size:13px}
</style>
