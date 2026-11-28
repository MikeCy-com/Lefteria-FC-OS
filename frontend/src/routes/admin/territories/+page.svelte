<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { page } from '$app/state';
  import { goto } from '$app/navigation';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import FootballLocationMetrics from '../../../lib/admin/FootballLocationMetrics.svelte';
  import AcademyLocationFinancePanel from '../../../lib/admin/AcademyLocationFinancePanel.svelte';
  import AcademySupplierSiteTotals from '../../../lib/admin/AcademySupplierSiteTotals.svelte';
  import FootballTerritoryAccessPanel from '../../../lib/admin/FootballTerritoryAccessPanel.svelte';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';

  type Owner = 'lefteria_fc_academy' | 'lefteria_fc';
  type Named = { id:string; name:string };
  type City = Named & { slug:string; is_active:boolean };
  type Location = City & {
    city_id:string; address:string|null; google_maps_url:string|null; can_manage:boolean;
  };
  type Scope = {
    id:string; organization_id:Owner; academy_city_id:string|null;
    team_id:string|null; team_name:string|null; can_write_catalog:boolean;
  };
  type Group = Named & { city_id:string; location_id:string; scope_id:string };
  type Catalog = Named & {
    organization_id:Owner; scope_id:string; is_active:boolean;
    address?:string|null; google_maps_url?:string|null;
    academy_location_id?:string|null; academy_city_id?:string|null;
  };
  type MatchDraft = {
    id:string; scope_id:string; organization_id:Owner;
    academy_group_id:string|null; academy_location_id:string|null;
    club_team_id:string|null; opponent_id:string; playing_field_id:string;
    match_date:string; kickoff_local:string; status:'draft';
    home_game:boolean; competition:string;
  };
  type Directory = {
    organization_id:Owner; cities:City[]; locations:Location[];
    scopes:Scope[]; groups:Group[]; unassigned_groups:Named[];
    unscoped_teams:Named[]; opponents:Catalog[];
    playing_fields:Catalog[]; training_bases:Catalog[]; match_drafts:MatchDraft[];
  };
  type Resource = 'opponent'|'playing_field'|'training_base';
  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);
  const owners = $derived(
    ($auth.session?.user.organizations ?? []).map(org => org.id)
      .filter((id):id is Owner=>id==='lefteria_fc' || id==='lefteria_fc_academy')
  );
  let organization = $state<Owner>('lefteria_fc_academy');
  let appliedRouteScope=false;
  let appliedRouteGroup=false;
  let placedGroupId=$state('');
  let pendingCityId=$state('');
  let pendingLocationId=$state('');
  let appliedDirectoryLink=false;
  const requestedGroup=$derived(page.url.searchParams.get('assign_group')??'');
  const requestedTeam=$derived(page.url.searchParams.get('team_id')??'');

  const allowed = $derived(
    $auth.status==='authenticated' && $auth.session?.user.role
      && ['manager','super_admin'].includes($auth.session.user.role)
      && owners.includes(organization)
      && hasAdminPermission($auth.session.user.permissions,
        organization==='lefteria_fc'?'teams:read':'academy:read')
  );
  const canWrite = $derived(Boolean(allowed && $auth.session &&
    hasAdminPermission($auth.session.user.permissions,
      organization==='lefteria_fc'?'teams:write':'academy:write')));
  let directory = $state<Directory|null>(null);
  let loading = $state(false);
  let saving = $state(false);
  let message = $state('');
  let error = $state('');
  let revision = $state(0);
  let cityId = $state('');
  let locationId = $state('');
  let scopeId = $state('');
  let kind = $state<Resource>('opponent');
  let academyOpponentName=$state('');
  let academyFieldName=$state('');
  let academyFieldAddress=$state('');
  let academyFieldMapsUrl=$state('');
  let name = $state('');
  let cityName=$state('');
  let citySlug=$state('');
  let locationName=$state('');
  let locationSlug=$state('');
  let locationAddress=$state('');
  let locationMapsUrl=$state('');
  let slug = $state('');
  let address = $state('');
  let googleMapsUrl = $state('');
  let selectedGroup = $state('');
  let unscopedTeam = $state('');
  let opponentId = $state('');
  let fieldId = $state('');
  let playingFieldId = $state('');
  let matchDate = $state('');
  let matchTime = $state('17:00');
  let competition = $state('');
  let homeGame = $state(true);

  const validNamed=(item:unknown):item is Named=>!!item &&
    typeof item==='object' && !Array.isArray(item) &&
    typeof (item as Named).id==='string' &&
    typeof (item as Named).name==='string';
  function validDirectory(value:unknown, owner:Owner):value is Directory {
    if(!value || typeof value!=='object' || Array.isArray(value)) return false;
    const x=value as Record<string,unknown>;
    if(x.organization_id!==owner) return false;
    for(const key of ['cities','locations','scopes','groups','unassigned_groups',
      'unscoped_teams','opponents','playing_fields','training_bases','match_drafts']) {
      if(!Array.isArray(x[key]) || (x[key] as unknown[]).length>3000) return false;
    }
    if(!['cities','locations','groups','unassigned_groups','unscoped_teams',
      'opponents','playing_fields','training_bases'].every(key=>
        (x[key] as unknown[]).every(validNamed))) return false;
    const scopes=x.scopes as Scope[];
    return scopes.every(item=>typeof item.id==='string' &&
      item.organization_id===owner && typeof item.can_write_catalog==='boolean') &&
      (x.locations as Location[]).every(item=>typeof item.city_id==='string' &&
        typeof item.can_manage==='boolean') &&
      (x.opponents as Catalog[]).every(item=>item.organization_id===owner) &&
      (x.playing_fields as Catalog[]).every(item=>item.organization_id===owner) &&
      (x.training_bases as Catalog[]).every(item=>item.organization_id===owner) &&
      (x.match_drafts as MatchDraft[]).every(item=>item.organization_id===owner &&
        item.status==='draft');
  }

  const cities = $derived(directory?.cities ?? []);
  const locations = $derived((directory?.locations ?? []).filter(l=>l.city_id===cityId));
  const selectedScope = $derived(organization==='lefteria_fc_academy'
    ? directory?.scopes.find(s=>s.academy_city_id===cityId)
    : directory?.scopes.find(s=>s.id===scopeId));
  const selectedLocation = $derived(locations.find(l=>l.id===locationId));
  const groups = $derived((directory?.groups ?? []).filter(g=>
    g.scope_id===selectedScope?.id && g.location_id===locationId));
  const opponents = $derived((directory?.opponents ?? []).filter(x=>
    x.scope_id===selectedScope?.id && x.is_active));
  const fields = $derived((directory?.playing_fields ?? []).filter(x=>
    x.scope_id===selectedScope?.id && x.is_active));
  const bases = $derived((directory?.training_bases ?? []).filter(x=>
    x.scope_id===selectedScope?.id && x.is_active &&
      (organization==='lefteria_fc' || x.academy_location_id===locationId)));
  const drafts = $derived((directory?.match_drafts ?? []).filter(x=>
    x.scope_id===selectedScope?.id &&
      (organization==='lefteria_fc' || x.academy_location_id===locationId)));
  const mayManageCatalog = $derived(Boolean(canWrite && selectedScope?.can_write_catalog));
  const mayManageLocation = $derived(Boolean(canWrite && selectedLocation?.can_manage));
  const mayManageDraft = $derived(organization==='lefteria_fc'
    ? mayManageCatalog : mayManageLocation);
  const canAddResource = $derived(kind==='training_base'
    ? mayManageDraft : mayManageCatalog);

  $effect(()=>{
    if(!browser) return;
    if($auth.status==='anonymous') void goto('/admin/login',{replace:true});
  });
  $effect(()=>{
    // Only apply a deep link once. A later manual organization selection must
    // never be overridden by stale URL parameters.
    if(!browser||appliedRouteScope||owners.length===0)return;
    const requested=page.url.searchParams.get('organization_id');
    if((requested==='lefteria_fc'||requested==='lefteria_fc_academy')&&owners.includes(requested))
      organization=requested;
    appliedRouteScope=true;
  });
  $effect(()=>{
    if(!owners.includes(organization) && owners.length>0) organization=owners[0];
  });
  $effect(()=>{
    const selected=organization;
    const actor=$auth.session?.user.id;
    const allowedNow=allowed;
    void revision;
    directory=null; loading=false; error='';
    if(!browser || !allowedNow || !actor) return;
    const abort=new AbortController();
    loading=true;
    void (async()=>{
      try {
        const response=await fetch('/api/admin/football/territories?organization_id='+
          encodeURIComponent(selected),{
          credentials:'same-origin',cache:'no-store',
          headers:{Accept:'application/json'},signal:abort.signal
        });
        if(response.status===401 || response.status===403){
          void auth.bootstrap(); throw new Error('Η πρόσβαση άλλαξε. Συνδεθείτε ξανά.');
        }
        if(!response.ok) throw new Error('Ο κατάλογος δεν είναι διαθέσιμος.');
        const data:unknown=await response.json();
        if(!validDirectory(data,selected)) throw new Error('Μη επαληθευμένη απάντηση καταλόγου.');
        if(!abort.signal.aborted && organization===selected &&
          $auth.session?.user.id===actor) directory=data;
      }catch(e){
        if(!abort.signal.aborted) error=e instanceof Error?e.message:'Αποτυχία φόρτωσης.';
      }finally{if(!abort.signal.aborted) loading=false;}
    })();
    return ()=>abort.abort();
  });
  $effect(()=>{
    if(!directory||appliedRouteGroup)return;
    if(organization==='lefteria_fc_academy' && requestedGroup &&
      directory.unassigned_groups.some(item=>item.id===requestedGroup)){
      selectedGroup=requestedGroup;
      appliedRouteGroup=true;
    }else if(organization==='lefteria_fc' && requestedTeam) {
      const matching=directory.scopes.find(item=>item.team_id===requestedTeam);
      if(matching){scopeId=matching.id;appliedRouteGroup=true;}
      else if(directory.unscoped_teams.some(item=>item.id===requestedTeam)){
        unscopedTeam=requestedTeam;appliedRouteGroup=true;
      }
    }
  });
  $effect(()=>{
    if(!directory || directory.organization_id!==organization)return;
    if(organization==='lefteria_fc_academy'){
      const requestedCity=pendingCityId||(!appliedDirectoryLink?page.url.searchParams.get('city_id')??'':'');
      const requestedLocation=pendingLocationId||(!appliedDirectoryLink?page.url.searchParams.get('location_id')??'':'');
      const verifiedLocation=directory.locations.find(item=>item.id===requestedLocation);
      if(verifiedLocation && cities.some(city=>city.id===verifiedLocation.city_id))cityId=verifiedLocation.city_id;
      else if(cities.some(city=>city.id===requestedCity))cityId=requestedCity;
      else if(!cities.some(city=>city.id===cityId))cityId=cities[0]?.id??'';
      if(verifiedLocation && verifiedLocation.city_id===cityId)locationId=verifiedLocation.id;
      else if(!locations.some(location=>location.id===locationId))locationId=locations[0]?.id??'';
      pendingCityId='';pendingLocationId='';appliedDirectoryLink=true;
    }else if(!(directory?.scopes ?? []).some(scope=>scope.id===scopeId))
      scopeId=directory?.scopes[0]?.id??'';
  });

  async function createResource(action:string, data:Record<string,unknown>) {
    if(!browser || !canWrite || saving || !directory) return;
    const owner=organization, actor=$auth.session?.user.id;
    saving=true; error=''; message='';
    try{
      const response=await fetch('/api/admin/football/territories?organization_id='+
        encodeURIComponent(owner),{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({kind:action,data})
      });
      if(actor!==$auth.session?.user.id || organization!==owner) return;
      if(response.status===401 || response.status===403){
        void auth.bootstrap(); throw new Error('Ο λογαριασμός ή το δικαίωμα άλλαξε.');
      }
      if(!response.ok) throw new Error(response.status===409
        ? 'Υπάρχει ήδη καταχώριση ή η ανάθεση άλλαξε. Ανανεώστε.'
        : 'Η εγγραφή δεν αποθηκεύτηκε. Ελέγξτε δικαιώματα, ονόματα και επιλογές.');
      const result:unknown=await response.json();
      if(!result || typeof result!=='object' || Array.isArray(result) ||
        (result as Record<string,unknown>).kind!==action ||
        (result as Record<string,unknown>).organization_id!==owner ||
        typeof (result as Record<string,unknown>).id!=='string')
        throw new Error('Μη επαληθευμένη αποθήκευση.');
      const createdId=(result as Record<string,unknown>).id as string;
      directory=null;
      if(action==='city'){pendingCityId=createdId;cityName='';citySlug='';}
      else if(action==='location'){pendingLocationId=createdId;locationName='';locationSlug='';locationAddress='';locationMapsUrl='';}
      else {name='';slug='';address='';googleMapsUrl='';competition='';}
      if(action==='group_assignment' && typeof data.group_id==='string')placedGroupId=data.group_id;
      message=action==='group_assignment'
        ? 'Το τμήμα συνδέθηκε επιτυχώς με την πόλη και τοποθεσία.'
        : 'Η καταχώριση αποθηκεύτηκε. Εμφανίζεται μόνο σε εξουσιοδοτημένους διαχειριστές.';
      revision++;
    }catch(e){error=e instanceof Error?e.message:'Αποτυχία αποθήκευσης.';}
    finally{saving=false;}
  }
  function submitLocation(){
    if(!cityId) return;
    void createResource('location',{city_id:cityId,name:locationName,slug:locationSlug,address:locationAddress,
      google_maps_url:locationMapsUrl});
  }
  async function submitAcademyCatalog(resource:'opponent'|'playing_field'){
    if(organization!=='lefteria_fc_academy'||!selectedScope||
      selectedScope.academy_city_id!==cityId||!mayManageCatalog||saving)return;
    const expectedCity=cityId;
    const enteredName=resource==='opponent'?academyOpponentName:academyFieldName;
    if(!enteredName.trim())return;
    // Keep unsaved form inputs on failed requests. The backend enforces the same
    // organization/city scope independently of these interface checks.
    const before=revision;
    await createResource(resource,{
      scope_id:selectedScope.id,name:enteredName.trim(),
      ...(resource==='playing_field'
        ?{address:academyFieldAddress.trim(),google_maps_url:academyFieldMapsUrl.trim()}: {})
    });
    if(organization=== 'lefteria_fc_academy' && cityId===expectedCity &&
      revision!==before){
      if(resource==='opponent')academyOpponentName='';
      else {academyFieldName='';academyFieldAddress='';academyFieldMapsUrl='';}
    }
  }
  function submitCatalog(){
    if(!selectedScope || !canAddResource) return;
    void createResource(kind,{
      scope_id:selectedScope.id,name,address,google_maps_url:googleMapsUrl,
      ...(kind==='training_base'&&organization==='lefteria_fc_academy'
        ?{location_id:locationId}:{}),
      ...(kind==='training_base'&&playingFieldId?{playing_field_id:playingFieldId}:{})
    });
  }
  function submitMatch(){
    if(!selectedScope || !mayManageDraft || !opponentId || !fieldId ||
      !matchDate || !matchTime ||
      (organization==='lefteria_fc_academy'&&!selectedGroup)) return;
    void createResource('match_draft',{
      scope_id:selectedScope.id,
      ...(organization==='lefteria_fc_academy'
        ?{location_id:locationId,group_id:selectedGroup}:{}),
      opponent_id:opponentId,playing_field_id:fieldId,
      match_date:matchDate,kickoff_local:matchTime,
      home_game:homeGame,competition
    });
  }
</script>

<svelte:head>
  <title>Πόλεις, εγκαταστάσεις & αγώνες | Λευτεριά FC</title>
  <meta name="robots" content="noindex, nofollow" />
</svelte:head>

{#if $auth.status==='authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="territories"
    busy={$auth.busy} onLogout={()=>void auth.logout()}>
    <div class="territory-page" data-testid="admin-football-territories">
      <header>
        
        <h1>ΠΟΛΕΙΣ <span>· ΤΟΠΟΘΕΣΙΕΣ · ΓΗΠΕΔΑ</span></h1>
        <p class="admin-muted">Οι αντίπαλοι και τα γήπεδα αγώνων της Ακαδημίας ανήκουν στην πόλη.
          Οι βάσεις προπονήσεων, τα τμήματα και οι αγώνες ανήκουν στην τοποθεσία.
          Η πρώτη ομάδα έχει απολύτως ανεξάρτητους καταλόγους.</p>
      </header>
      {#if requestedGroup && organization==='lefteria_fc_academy'}
        <section class="admin-panel territory-setup-callout" data-testid="territory-complete-group-setup">
          <h2>Ολοκλήρωση νέου τμήματος</h2>
          <p>Επιλέξτε ή δημιουργήστε πόλη, έπειτα τοποθεσία και τέλος
            αναθέστε το νέο τμήμα. Αντίπαλοι και γήπεδα αγώνων ανήκουν στην <strong>πόλη</strong>,
            ενώ βάσεις προπόνησης, προπονητές και τμήματα στην <strong>τοποθεσία</strong>.</p>
          <p class="admin-muted">Το τμήμα παραμένει μη ανατεθειμένο μέχρι ο διακομιστής να επιβεβαιώσει την ανάθεση.</p>
        </section>
      {:else if requestedTeam && organization==='lefteria_fc'}
        <section class="admin-panel territory-setup-callout" data-testid="territory-complete-team-setup">
          <h2>Υποδομές νέας ομάδας</h2>
          <p>Δημιουργήστε αγωνιστικό πεδίο για την ομάδα και καταχωρίστε
            τους δικούς της αντιπάλους, γήπεδα αγώνων και βάσεις προπόνησης.</p>
        </section>
      {/if}
      <section class="admin-panel" aria-label="Οργανισμός και ανανέωση">
        <div class="territory-row">
          <label>Οργανισμός
            <select bind:value={organization} data-testid="territory-organization">
              {#each owners as owner (owner)}
                <option value={owner}>{owner==='lefteria_fc_academy'?'Ακαδημία':'Πρώτη ομάδα'}</option>
              {/each}
            </select>
          </label>
          <button type="button" class="admin-secondary" disabled={loading||saving}
            onclick={()=>revision++}>Ανανέωση καταλόγου</button>
        </div>
      </section>

      {#if !allowed}
        <section class="admin-panel" role="alert">Δεν έχετε δικαίωμα πρόσβασης στον επιλεγμένο οργανισμό.</section>
      {:else if loading}
        <p role="status">Φόρτωση επαληθευμένου καταλόγου…</p>
      {:else if directory}
        {#if organization==='lefteria_fc_academy'}
          <section class="admin-panel" aria-labelledby="city-heading">
            <h2 id="city-heading">1. Πόλη Ακαδημίας</h2>
            <div class="territory-row">
              <label>Πόλη
                <select bind:value={cityId} data-testid="territory-city"
                  onchange={()=>{locationId='';selectedGroup='';opponentId='';fieldId='';playingFieldId='';}}>
                  {#each cities as city (city.id)}
                    <option value={city.id}>{city.name}{city.is_active?'':' (ανενεργή)'}</option>
                  {/each}
                </select>
              </label>
              <p class="admin-muted">Αντίπαλοι και γήπεδα αγώνων κοινοί για όλες τις τοποθεσίες αυτής της πόλης.</p>
            </div>
            {#if canWrite && $auth.session.user.role==='super_admin'}
              <form class="territory-form" id="create-city" data-testid="admin-create-city" onsubmit={e=>{
                e.preventDefault(); void createResource('city',{name:cityName,slug:citySlug});
              }}>
                <h3>Νέα πόλη</h3>
                <label>Όνομα πόλης <input bind:value={cityName} required maxlength="120" /></label>
                <label>Σύντομο όνομα URL (λατινικά) <input bind:value={citySlug} required maxlength="120"
                  pattern="[a-z0-9]+(-[a-z0-9]+)*" placeholder="nicosia" /></label>
                <button type="submit" disabled={saving}>Προσθήκη πόλης</button>
              </form>
            {/if}
          </section>

          {#if selectedScope}
            <section class="admin-panel academy-city-catalog" aria-labelledby="academy-city-catalog-heading"
              data-testid="academy-city-catalog">
              <header class="academy-city-catalog-title">
                <div>
                  <h2 id="academy-city-catalog-heading">Αντίπαλοι & γήπεδα πόλης</h2>
                  <p class="admin-muted">Κοινός κατάλογος για όλες τις τοποθεσίες και τα τμήματα της επιλεγμένης πόλης.
                    Η Πρώτη Ομάδα έχει ξεχωριστούς αντιπάλους και γήπεδα.</p>
                </div>
              </header>
              <div class="territory-columns">
                <div>
                  <h3>Αντίπαλοι πόλης</h3>
                  {#if opponents.length===0}<p class="admin-muted">Δεν έχουν καταχωριστεί αντίπαλοι σε αυτή την πόλη.</p>{/if}
                  <ul class="territory-list" data-testid="academy-city-opponents">
                    {#each opponents as item (item.id)}<li>{item.name}</li>{/each}
                  </ul>
                </div>
                <div>
                  <h3>Γήπεδα αγώνων πόλης</h3>
                  {#if fields.length===0}<p class="admin-muted">Δεν έχουν καταχωριστεί γήπεδα σε αυτή την πόλη.</p>{/if}
                  <ul class="territory-list" data-testid="academy-city-fields">
                    {#each fields as item (item.id)}
                      <li><span>{item.name}</span>
                        {#if item.google_maps_url}
                          <a href={item.google_maps_url} target="_blank" rel="noopener noreferrer">Google Maps</a>
                        {/if}
                      </li>
                    {/each}
                  </ul>
                </div>
              </div>
              {#if mayManageCatalog}
                <div class="territory-columns">
                  <form class="territory-form" data-testid="academy-add-city-opponent"
                    onsubmit={e=>{e.preventDefault();submitAcademyCatalog('opponent');}}>
                    <h3>Νέος αντίπαλος πόλης</h3>
                    <label>Όνομα αντιπάλου
                      <input bind:value={academyOpponentName} required maxlength="160" />
                    </label>
                    <button type="submit" disabled={saving||!academyOpponentName.trim()}>Προσθήκη αντιπάλου</button>
                  </form>
                  <form class="territory-form" data-testid="academy-add-city-field"
                    onsubmit={e=>{e.preventDefault();submitAcademyCatalog('playing_field');}}>
                    <h3>Νέο γήπεδο αγώνα πόλης</h3>
                    <label>Όνομα γηπέδου <input bind:value={academyFieldName} required maxlength="160" /></label>
                    <label>Διεύθυνση <input bind:value={academyFieldAddress} maxlength="300" /></label>
                    <label>Google Maps <input type="url" bind:value={academyFieldMapsUrl} maxlength="2048" /></label>
                    <button type="submit" disabled={saving||!academyFieldName.trim()}>Προσθήκη γηπέδου</button>
                  </form>
                </div>
              {/if}
            </section>
          {/if}

          {#if selectedScope}
            <section class="admin-panel" aria-labelledby="location-heading">
              <h2 id="location-heading">2. Τοποθεσία στην πόλη</h2>
              <label>Τοποθεσία
                <select bind:value={locationId} data-testid="territory-location"
                  onchange={()=>{selectedGroup='';playingFieldId='';}}>
                  {#each locations as location (location.id)}
                    <option value={location.id}>{location.name}{location.is_active?'':' (ανενεργή)'}</option>
                  {/each}
                </select>
              </label>
              <p class="admin-muted">Οι προπονήσεις, τα τμήματα και οι βάσεις της κάθε τοποθεσίας
                δεν εμφανίζονται σε άλλη τοποθεσία.</p>
              {#if mayManageCatalog}
                <form class="territory-form" id="create-location" data-testid="admin-create-location" onsubmit={e=>{e.preventDefault();submitLocation();}}>
                  <h3>Νέα τοποθεσία</h3>
                  <label>Όνομα <input bind:value={locationName} required maxlength="120" /></label>
                  <label>Σύντομο όνομα URL (λατινικά) <input bind:value={locationSlug} required maxlength="120"
                    pattern="[a-z0-9]+(-[a-z0-9]+)*" /></label>
                  <label>Διεύθυνση <input bind:value={locationAddress} maxlength="300" /></label>
                  <label>Google Maps <input type="url" bind:value={locationMapsUrl} maxlength="2048" /></label>
                  <button type="submit" disabled={saving}>Προσθήκη τοποθεσίας</button>
                </form>
              {/if}
              {#if selectedLocation}
                <FootballLocationMetrics organization="lefteria_fc_academy"
                  cityId={cityId} locationId={locationId}
                  actorId={$auth.session.user.id} revision={revision}
                  canManageCity={Boolean(selectedScope?.can_write_catalog)} />
                <AcademyLocationFinancePanel
                  actorId={$auth.session.user.id} {cityId} {locationId}
                  locations={directory.locations}
                  superAdmin={$auth.session.user.role==='super_admin'}
                  revision={revision}/>
                <AcademySupplierSiteTotals actorId={$auth.session.user.id}
                  {cityId} {locationId}/>
                <h3>3. Τμήματα τοποθεσίας</h3>
                {#if mayManageLocation && canWrite}
                  <a class="admin-secondary" href={`/admin/groups?organization_id=lefteria_fc_academy&new=1&location_id=${encodeURIComponent(locationId)}`} data-testid="territory-create-group-direct">+ Νέο τμήμα σε αυτή την τοποθεσία</a>
                {/if}
                {#if groups.length===0}<p class="admin-muted">Δεν έχουν ανατεθεί τμήματα σε αυτή την τοποθεσία.</p>
                {:else}<ul class="territory-list">{#each groups as group (group.id)}<li>{group.name}</li>{/each}</ul>{/if}
                {#if mayManageLocation && directory.unassigned_groups.length>0}
                  <form class="territory-form" id="group-placement" data-testid="territory-group-placement" onsubmit={e=>{
                    e.preventDefault();
                    if(selectedScope && selectedGroup) void createResource('group_assignment',{
                      scope_id:selectedScope.id,location_id:locationId,group_id:selectedGroup
                    });
                  }}>
                    <h3>Ανάθεση μη τοποθετημένου τμήματος</h3>
                    <label>Τμήμα
                      <select bind:value={selectedGroup} required>
                        <option value="">Επιλέξτε τμήμα</option>
                        {#each directory.unassigned_groups as group (group.id)}
                          <option value={group.id}>{group.name}</option>
                        {/each}
                      </select>
                    </label>
                    <button type="submit" disabled={saving || !selectedGroup}>Ανάθεση στην τοποθεσία</button>
                  </form>
                {/if}
              {/if}
            </section>
          {/if}
        {:else}
          <section class="admin-panel" aria-labelledby="team-heading">
            <h2 id="team-heading">Ομάδα συλλόγου</h2>
            <label>Επιλογή ομάδας
              <select bind:value={scopeId} data-testid="territory-team"
                onchange={()=>{opponentId='';fieldId='';playingFieldId='';}}>
                {#each directory.scopes as scope (scope.id)}
                  <option value={scope.id}>{scope.team_name ?? scope.team_id}</option>
                {/each}
              </select>
            </label>
            {#if canWrite && $auth.session.user.role==='super_admin' && directory.unscoped_teams.length>0}
              <form class="territory-form" onsubmit={e=>{
                e.preventDefault();if(unscopedTeam) void createResource('team_scope',{team_id:unscopedTeam});
              }}>
                <h3>Ενεργοποίηση ανεξάρτητου καταλόγου ομάδας</h3>
                <label>Ομάδα χωρίς κατάλογο
                  <select bind:value={unscopedTeam} required>
                    <option value="">Επιλέξτε ομάδα</option>
                    {#each directory.unscoped_teams as team (team.id)}
                      <option value={team.id}>{team.name}</option>
                    {/each}
                  </select>
                </label>
                <button disabled={saving || !unscopedTeam}>Δημιουργία καταλόγου</button>
              </form>
            {/if}
          </section>
        {/if}

        {#if selectedScope}
          {#if organization==='lefteria_fc'}
          <section class="admin-panel" aria-labelledby="catalog-heading" data-testid="first-team-catalog">
            <h2 id="catalog-heading">3. Αντίπαλοι και γήπεδα αγώνων
              ανά ομάδα</h2>
            <div class="territory-columns">
              <div>
                <h3>Αντίπαλοι</h3>
                {#if opponents.length===0}<p class="admin-muted">Δεν υπάρχουν αποθηκευμένοι αντίπαλοι.</p>{/if}
                <ul class="territory-list">{#each opponents as item (item.id)}
                  <li>{item.name}</li>{/each}</ul>
              </div>
              <div>
                <h3>Γήπεδα αγώνων</h3>
                {#if fields.length===0}<p class="admin-muted">Δεν υπάρχουν αποθηκευμένα γήπεδα.</p>{/if}
                <ul class="territory-list">{#each fields as item (item.id)}
                  <li>{item.name}
                    {#if item.google_maps_url}<a href={item.google_maps_url}
                      target="_blank" rel="noopener noreferrer">Χάρτης</a>{/if}
                  </li>{/each}</ul>
              </div>
            </div>
            {#if mayManageCatalog}
              <form class="territory-form" onsubmit={e=>{e.preventDefault();submitCatalog();}}>
                <h3>Προσθήκη κοινού καταλόγου ομάδας</h3>
                <label>Τύπος
                  <select bind:value={kind}>
                    <option value="opponent">Αντίπαλος</option>
                    <option value="playing_field">Γήπεδο αγώνα</option>
                  </select>
                </label>
                <label>Όνομα <input bind:value={name} required maxlength="160" /></label>
                {#if kind==='playing_field'}
                  <label>Διεύθυνση <input bind:value={address} maxlength="300" /></label>
                  <label>Google Maps <input type="url" bind:value={googleMapsUrl} maxlength="2048" /></label>
                {/if}
                <button type="submit" disabled={saving}>Αποθήκευση</button>
              </form>
            {/if}
          </section>

          {/if}

          <section class="admin-panel" aria-labelledby="base-heading">
            <h2 id="base-heading">4. Βάσεις προπονήσεων
              {organization==='lefteria_fc_academy'?'της τοποθεσίας':'της ομάδας'}</h2>
            {#if bases.length===0}<p class="admin-muted">Δεν υπάρχουν αποθηκευμένες βάσεις.</p>{/if}
            <ul class="territory-list">{#each bases as item (item.id)}
              <li>{item.name}
                {#if item.google_maps_url}<a href={item.google_maps_url}
                  target="_blank" rel="noopener noreferrer">Χάρτης</a>{/if}
              </li>{/each}</ul>
            {#if mayManageDraft}
              <form class="territory-form" onsubmit={e=>{
                e.preventDefault(); if(selectedScope) void createResource('training_base',{
                  scope_id:selectedScope.id,name,address,
                  google_maps_url:googleMapsUrl,
                  ...(organization==='lefteria_fc_academy'?{location_id:locationId}:{}),
                  ...(playingFieldId?{playing_field_id:playingFieldId}:{})
                });
              }}>
                <h3>Νέα βάση προπονήσεων</h3>
                <label>Όνομα <input bind:value={name} required maxlength="160" /></label>
                <label>Διεύθυνση <input bind:value={address} maxlength="300" /></label>
                <label>Google Maps <input type="url" bind:value={googleMapsUrl} maxlength="2048" /></label>
                <label>Συνδεδεμένο γήπεδο (προαιρετικό)
                  <select bind:value={playingFieldId}>
                    <option value="">Χωρίς σύνδεση</option>
                    {#each fields as field (field.id)}<option value={field.id}>{field.name}</option>{/each}
                  </select>
                </label>
                <button type="submit" disabled={saving}>Αποθήκευση βάσης</button>
              </form>
            {/if}
          </section>

          <section class="admin-panel" aria-labelledby="draft-heading">
            <h2 id="draft-heading">5. Πρόχειροι αγώνες</h2>
            <p class="admin-muted">Η δημιουργία εδώ αποθηκεύει εσωτερικό πρόχειρο αγώνα,
              χωρίς να τον δημοσιεύει στο πρόγραμμα ή να ενημερώνει αποτελέσματα.</p>
            {#if drafts.length===0}<p class="admin-muted">Δεν υπάρχουν πρόχειροι αγώνες.</p>{/if}
            <ul class="territory-list">{#each drafts as draft (draft.id)}
              <li><time datetime={draft.match_date}>{draft.match_date}</time> ·
                {draft.kickoff_local} · {opponents.find(o=>o.id===draft.opponent_id)?.name??'—'}
              </li>{/each}</ul>
            {#if mayManageDraft}
              <form class="territory-form" onsubmit={e=>{e.preventDefault();submitMatch();}}>
                {#if organization==='lefteria_fc_academy'}
                  <label>Τμήμα της τοποθεσίας
                    <select bind:value={selectedGroup} required>
                      <option value="">Επιλέξτε τμήμα</option>
                      {#each groups as group (group.id)}<option value={group.id}>{group.name}</option>{/each}
                    </select>
                  </label>
                {/if}
                <label>Αντίπαλος
                  <select bind:value={opponentId} required>
                    <option value="">Επιλέξτε αντίπαλο</option>
                    {#each opponents as item (item.id)}<option value={item.id}>{item.name}</option>{/each}
                  </select>
                </label>
                <label>Γήπεδο αγώνα
                  <select bind:value={fieldId} required>
                    <option value="">Επιλέξτε γήπεδο</option>
                    {#each fields as item (item.id)}<option value={item.id}>{item.name}</option>{/each}
                  </select>
                </label>
                <label>Ημερομηνία <input type="date" bind:value={matchDate} required /></label>
                <label>Ώρα έναρξης <input type="time" bind:value={matchTime} required /></label>
                <label>Διοργάνωση <input bind:value={competition} maxlength="160" /></label>
                <label class="territory-checkbox"><input type="checkbox" bind:checked={homeGame} /> Εντός έδρας</label>
                <button type="submit" disabled={saving || !opponentId || !fieldId ||
                  (organization==='lefteria_fc_academy'&&!selectedGroup)}>Αποθήκευση προχείρου</button>
              </form>
            {/if}
          </section>
        {/if}
      {:else if !error}
        <section class="admin-panel">Δεν υπάρχουν ακόμη επιβεβαιωμένες αναθέσεις.</section>
      {/if}
      {#if directory && $auth.session.user.role==='super_admin' && canWrite}
        <div id="delegated-access" class="territory-delegation-anchor"
          data-testid="territory-role-permissions">
          <FootballTerritoryAccessPanel {organization}
            actorId={$auth.session.user.id} cities={directory.cities}
            locations={directory.locations} scopes={directory.scopes}
            focusAccountId={page.url.searchParams.get('account_id')??''}
            revision={revision}/>
        </div>
      {/if}
      {#if error}<p class="territory-error" role="alert">{error}</p>{/if}
      {#if message}<p class="territory-success" role="status">{message}</p>{/if}
      {#if placedGroupId && organization==='lefteria_fc_academy'}
        <a class="territory-setup-return"
          href={`/admin/groups?group_id=${encodeURIComponent(placedGroupId)}`}
          data-testid="territory-return-to-created-group">Άνοιγμα τμήματος & εργασιών ↗</a>
      {/if}
    </div>
  </AdminFrame>
{/if}

<style>
  .territory-setup-callout{background:linear-gradient(110deg,#2c2312,#0e1213);
    border:1px solid #bc9147;padding:20px}
  .territory-setup-callout h2{margin:0 0 9px;font-size:25px}
  .territory-setup-callout p{line-height:1.5;max-width:85ch}
  .territory-setup-return{display:inline-flex;align-items:center;min-height:44px;
    color:#ffd16b;background:#171a18;border:1px solid #ad863a;padding:10px 16px;
    text-decoration:none;font-weight:800}
  #group-placement{scroll-margin-top:90px}
  .territory-page{display:grid;gap:1.3rem;width:100%;min-width:0}
  .academy-city-catalog-title{margin-bottom:12px}
  .academy-city-catalog-title p{max-width:75ch}
  .academy-city-catalog .territory-columns{margin-top:12px}
  .academy-city-catalog .territory-list li{min-height:42px;align-items:center}
  .academy-city-catalog .territory-form{min-width:0}
  .academy-city-catalog .territory-form input{max-width:100%}
  .territory-page h1{font-size:clamp(1.65rem,3vw,2.75rem);letter-spacing:-.045em;margin:.4rem 0}
  .territory-page h1 span{color:#bd9763}
  .territory-page h2{font-size:1.2rem;margin:0 0 1rem}
  .territory-page h3{font-size:1rem;margin:.7rem 0}
  .territory-row{display:flex;flex-wrap:wrap;align-items:end;gap:1.2rem}
  .territory-row>*{flex:1 1 250px}
  .territory-columns{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:1.4rem}
  .territory-page label{display:grid;gap:.45rem;font-weight:650;font-size:.91rem}
  .territory-page input,.territory-page select{width:100%;max-width:550px;min-height:44px;
    border:1px solid #8c806b;border-radius:.5rem;background:transparent;color:inherit;padding:.7rem}
  .territory-page input:focus-visible,.territory-page select:focus-visible,
  .territory-page button:focus-visible,.territory-page a:focus-visible{
    outline:3px solid #bd9763;outline-offset:3px}
  .territory-page button{min-height:44px;cursor:pointer}
  .territory-page button:disabled{opacity:.55;cursor:not-allowed}
  #create-city,#create-location,#delegated-access{scroll-margin-top:100px}
  .territory-form{display:grid;gap:1rem;padding:1.2rem 0;margin-top:1rem;
    border-top:1px solid rgba(160,140,100,.35)}
  .territory-form button{justify-self:start}
  .territory-list{display:grid;gap:.55rem;list-style:none;margin:0;padding:0}
  .territory-list li{padding:.65rem .8rem;border:1px solid rgba(160,140,100,.3);
    border-radius:.5rem;display:flex;gap:1rem;flex-wrap:wrap;justify-content:space-between}
  .territory-checkbox{display:flex!important;align-items:center}
  .territory-checkbox input{min-height:auto;width:22px}
  .territory-error{color:#c42a2a;font-weight:700}
  .territory-success{color:inherit;border-inline-start:4px solid #bd9763;padding:.7rem}
</style>
