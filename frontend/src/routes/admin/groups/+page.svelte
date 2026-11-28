<script lang="ts">
  import { getContext } from 'svelte';
  import { browser } from '$app/env';
  import { page } from '$app/state';
  import { goto } from '$app/navigation';
  import { ADMIN_AUTH_CONTEXT, type AdminAuthController } from '../../../lib/admin/controller.ts';
  import { hasAdminPermission } from '../../../lib/admin/permissions.ts';
  import AdminFrame from '../../../lib/admin/AdminFrame.svelte';
  import AdminCrmWorkspaceNav from '../../../lib/admin/AdminCrmWorkspaceNav.svelte';
  import { scopedAdminOrganization } from '../../../lib/admin/admin-organization.ts';
  import AdminSessionStatus from '../../../lib/admin/AdminSessionStatus.svelte';
  import AdminGroupWorkspace from '../../../lib/admin/AdminGroupWorkspace.svelte';
  import AdminRosterPlayersPanel from '../../../lib/admin/AdminRosterPlayersPanel.svelte';

  type Organization = 'lefteria_fc' | 'lefteria_fc_academy';
  type Audience = { id: string; name: string; kind: 'team' | 'academy_group'; active_players: number };
  type Directory = {organization_id: Organization; ready: true; unassigned_players: number; groups: Audience[]};
  type CreationLocation = {id:string;city_id:string;label:string};
  type CreationCity = {id:string;name:string;canCreateLocation:boolean};
  type GroupPlacement = {cityId:string;locationId:string;cityName:string;locationName:string};
  type AcademyCardProfile = {group_id:string;organization_id:string;age_range:string;
    coach_name:string;training_schedule:string};
  const auth = getContext<AdminAuthController>(ADMIN_AUTH_CONTEXT);

  const grantedAssignments = $derived(
    $auth.status === 'authenticated' && $auth.session
      ? $auth.session.user.organizations.filter((org): org is typeof org & {id: Organization} =>
          (org.id === 'lefteria_fc' && hasAdminPermission($auth.session!.user.permissions, 'teams:read')) ||
          (org.id === 'lefteria_fc_academy' && hasAdminPermission($auth.session!.user.permissions, 'academy:read')))
      : []
  );

  // A scoped menu route is a local workspace, not a global organization switch.
  const routeOwner=$derived(page.url.searchParams.get('organization_id'));
  const assignments=$derived(routeOwner===null?grantedAssignments:
    grantedAssignments.filter(org=>org.id===routeOwner));
  let organization = $state<Organization | null>(null);
  let directory = $state<Directory | null>(null);
  let otherDirectory=$state<Directory|null>(null);
  let otherLoading=$state(false);
  let otherUnavailable=$state(false);
  const otherOwner=$derived(routeOwner===null?assignments.find(org=>org.id!==organization)?.id??null:null);
  // Read-only cross-organization overview; all writes remain in the exact owner workspace.
  const combinedDirectories=$derived(assignments.map(org=>({
    owner:org.id,name:org.name,directory:org.id===organization?directory:otherDirectory?.organization_id===org.id?otherDirectory:null,
    loading:org.id===organization?loading:otherLoading,unavailable:org.id===organization?unavailable:otherUnavailable
  })));
  let focusedGroupId = $state('');
  let groupSearch = $state('');
  let groupPlacements=$state<Record<string,GroupPlacement>>({});
  let placementsReady=$state(false);
  let cityFilter=$state('');
  let siteFilter=$state('');
  let directoryView=$state<'groups'|'players'>('groups');
  const requestedPlayerId=$derived(organization==='lefteria_fc_academy'?page.url.searchParams.get('player_id'):null);
  let academyCardProfiles=$state<Record<string,AcademyCardProfile>>({});
  const placementCities=$derived([...new Map(Object.values(groupPlacements)
    .map(p=>[p.cityId,p.cityName])).entries()].sort((a,b)=>a[1].localeCompare(b[1],'el')));
  const placementSites=$derived([...new Map(Object.values(groupPlacements)
    .filter(p=>!cityFilter||p.cityId===cityFilter)
    .map(p=>[p.locationId,p.locationName])).entries()].sort((a,b)=>a[1].localeCompare(b[1],'el')));
  const visibleGroups=$derived((directory?.groups ?? []).filter(group=>{
    const p=groupPlacements[group.id],term=groupSearch.trim().toLocaleLowerCase('el-GR');
    if(organization==='lefteria_fc_academy' &&
      ((cityFilter && p?.cityId!==cityFilter) ||
       (siteFilter && p?.locationId!==siteFilter)))return false;
    return !term||[group.name,p?.cityName??'',p?.locationName??'']
      .some(value=>value.toLocaleLowerCase('el-GR').includes(term));
  }).sort((a,b)=>{
    if(organization!=='lefteria_fc_academy')return a.name.localeCompare(b.name,'el');
    const pa=groupPlacements[a.id],pb=groupPlacements[b.id];
    return (pa?.cityName??'Ω').localeCompare(pb?.cityName??'Ω','el')||
      (pa?.locationName??'Ω').localeCompare(pb?.locationName??'Ω','el')||
      a.name.localeCompare(b.name,'el');
  }));
  const focusedGroup = $derived(directory?.groups.find(item=>item.id===focusedGroupId)??null);
  const canSeeAcademyAttendance = $derived(Boolean($auth.session &&
    ['manager','super_admin'].includes($auth.session.user.role) &&
    organization==='lefteria_fc_academy' &&
    hasAdminPermission($auth.session.user.permissions,'attendance:read')));
  let loading = $state(false);
  let unavailable = $state(false);
  let retry = $state(0);
  let showCreate = $state(false);
  let groupName = $state('');
  let createRequestId = $state('');
  let teamLevel = $state("Α' Ομάδα");
  let teamDescription = $state('');
  let teamBanner = $state('');
  let academyAge = $state('');
  let academyCoach = $state('');
  let academySchedule = $state('');
  let academyDescription = $state('');
  let academyCapacity = $state(25);
  let academySeason = $state('');
  let academyBanner = $state('');
  let academyOrder = $state(0);
  let saving = $state(false);
  let createError = $state<string | null>(null);
  let lastCreatedGroupId=$state('');
  let lastCreatedOrganization=$state<Organization|null>(null);
  let lastCreatedLocation=$state<CreationLocation|null>(null);
  let creationLocations=$state<CreationLocation[]>([]);
  let creationCities=$state<CreationCity[]>([]);
  let creationCityId=$state('');
  let locationReload=$state(0);
  let pendingLocationId=$state('');
  let showInlineLocation=$state(false);
  let locationName=$state('');
  let locationSlug=$state('');
  let locationAddress=$state('');
  let locationMapsUrl=$state('');
  let locationSaving=$state(false);
  let locationCreateError=$state('');
  const cityLocations=$derived(creationLocations.filter(site=>site.city_id===creationCityId));
  const mayCreateLocation=$derived(creationCities.some(city=>
    city.id===creationCityId && city.canCreateLocation));
  let creationLocationId=$state('');
  let creationLocationsLoading=$state(false);
  let creationLocationsError=$state('');

  let lastRequestedGroup:string|undefined;
  $effect(()=>{
    const requested=page.url.searchParams.get('group_id')??'';
    if(!directory||directory.organization_id!==organization)return;
    const available=directory.groups;
    if(focusedGroupId && !available.some(group=>group.id===focusedGroupId))focusedGroupId='';
    if(requested!==lastRequestedGroup){
      lastRequestedGroup=requested;
      if(available.some(group=>group.id===requested))focusedGroupId=requested;
    }
  });
  let appliedInitialOrganization=false;
  let openedRequestedCreate=false;
  let appliedRequestedView=false;
  $effect(()=>{
    if(!browser||appliedRequestedView||!canCreate)return;
    appliedRequestedView=true;
    if(page.url.searchParams.get('view')==='players')directoryView='players';
  });
  $effect(()=>{
    const choices=assignments;
    if(!browser||appliedInitialOrganization||$auth.status!=='authenticated'||!choices.length)return;
    appliedInitialOrganization=true;
    const requested=page.url.searchParams.get('organization_id');
    if((requested==='lefteria_fc'||requested==='lefteria_fc_academy')&&
      choices.some(item=>item.id===requested))organization=requested;
  });

  const canCreate = $derived(
    $auth.status === 'authenticated' && $auth.session && !!organization &&
    assignments.some(org => org.id === organization) &&
    hasAdminPermission($auth.session.user.permissions,
      organization === 'lefteria_fc' ? 'teams:write' : 'academy:write') &&
    (organization !== 'lefteria_fc' || $auth.session.user.role==='super_admin')
  );

  // The group directory exposes only names and counts. Optional card details
  // come from the existing private, group-specific and independently authorized
  // profile endpoint. Bound the request fan-out and reject stale role responses.
  $effect(()=>{
    const owner=organization,actor=$auth.session?.user.id;
    const groups=directory?.groups ?? [],view=directoryView,allowed=canCreate;
    academyCardProfiles={};
    if(!browser||owner!=='lefteria_fc_academy'||!actor||!allowed||
      view!=='groups'||groups.length===0)return;
    const controller=new AbortController();
    void (async()=>{
      const rows=groups.filter(group=>group.kind==='academy_group').slice(0,12);
      const result:Record<string,AcademyCardProfile>={};
      await Promise.all(rows.map(async group=>{
        try{
          const response=await fetch('/api/admin/football/academy-group-profiles/'+
            encodeURIComponent(group.id)+'?organization_id=lefteria_fc_academy',{
              credentials:'same-origin',cache:'no-store',signal:controller.signal
            });
          if(response.status===401){controller.abort();auth.invalidateSession();return;}
          if(response.status===403){controller.abort();void auth.bootstrap();return;}
          if(!response.ok)return;
          const raw:unknown=await response.json();
          if(!raw || typeof raw!=='object' || Array.isArray(raw))return;
          const profile=raw as Record<string,unknown>;
          if(profile.organization_id!=='lefteria_fc_academy'||
            profile.group_id!==group.id ||
            !['age_range','coach_name','training_schedule'].every(key=>
              typeof profile[key]==='string' && (profile[key] as string).length<=500))return;
          result[group.id]={group_id:group.id,organization_id:'lefteria_fc_academy',
            age_range:profile.age_range as string,coach_name:profile.coach_name as string,
            training_schedule:profile.training_schedule as string};
        }catch{ /* Per-card metadata is optional; never block the canonical directory. */ }
      }));
      if(!controller.signal.aborted && organization===owner &&
        $auth.session?.user.id===actor && directoryView===view)academyCardProfiles=result;
    })();
    return ()=>controller.abort();
  });


  // Group ownership comes ONLY from independently authorized exact territory
  // assignments. Missing placement must stay unknown, never inferred from name.
  $effect(()=>{
    const owner=organization,actor=$auth.session?.user.id,view=directoryView;
    const groups=directory?.groups??[],allowed=canCreate;
    groupPlacements={};placementsReady=false;cityFilter='';siteFilter='';
    if(!browser||owner!=='lefteria_fc_academy'||!actor||!allowed||
       view!=='groups'||!directory||!groups.length)return;
    const controller=new AbortController();
    void(async()=>{
      try{
        const response=await fetch('/api/admin/football/territories?organization_id=lefteria_fc_academy',{
          credentials:'same-origin',cache:'no-store',signal:controller.signal
        });
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();return;}
        if(!response.ok)return;
        const raw:unknown=await response.json();
        if(!raw||typeof raw!=='object'||Array.isArray(raw))return;
        const data=raw as Record<string,unknown>;
        if(data.organization_id!=='lefteria_fc_academy'||
          !Array.isArray(data.cities)||!Array.isArray(data.locations)||
          !Array.isArray(data.groups)||data.groups.length>3000)return;
        const cities=new Map<string,string>();
        for(const row of data.cities){
          if(row&&typeof row==='object'&&!Array.isArray(row) &&
            typeof row.id==='string'&&typeof row.name==='string'&&row.is_active===true)
            cities.set(row.id,row.name);
        }
        const locations=new Map<string,{cityId:string;name:string}>();
        for(const row of data.locations){
          if(row&&typeof row==='object'&&!Array.isArray(row)&&
            typeof row.id==='string'&&typeof row.city_id==='string'&&
            typeof row.name==='string'&&row.is_active===true&&
            cities.has(row.city_id))
            locations.set(row.id,{cityId:row.city_id,name:row.name});
        }
        const visible=new Set(groups.map(g=>g.id));
        const proven:Record<string,GroupPlacement>={};
        for(const row of data.groups){
          if(!row||typeof row!=='object'||Array.isArray(row)||
            typeof row.id!=='string'||!visible.has(row.id)||
            typeof row.city_id!=='string'||typeof row.location_id!=='string')continue;
          const site=locations.get(row.location_id);
          if(!site||site.cityId!==row.city_id||!cities.has(row.city_id))continue;
          proven[row.id]={cityId:row.city_id,locationId:row.location_id,
            cityName:cities.get(row.city_id)!,locationName:site.name};
        }
        if(controller.signal.aborted||organization!==owner||
          $auth.session?.user.id!==actor||directoryView!==view)return;
        groupPlacements=proven;placementsReady=true;
      }catch{ /* Keep the verified roster usable without guessed geography. */ }
    })();
    return()=>controller.abort();
  });

  // The server returns only currently authorized Academy locations. This
  // selector does not infer city/site authority from any editable text or URL.
  $effect(() => {
    const owner=organization,active=showCreate,actor=$auth.session?.user.id;
    const generation=locationReload;void generation;
    creationLocations=[];creationLocationId='';creationLocationsError='';
    creationLocationsLoading=false;
    if(!browser || !active || owner!=='lefteria_fc_academy' || !canCreate)return;
    const controller=new AbortController();
    creationLocationsLoading=true;
    void (async()=>{
      try{
        const response=await fetch('/api/admin/football/territories?organization_id=lefteria_fc_academy',{
          credentials:'same-origin',cache:'no-store',signal:controller.signal
        });
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();throw new Error('Τα δικαιώματα τοποθεσίας άλλαξαν.');}
        if(!response.ok)throw new Error('Οι τοποθεσίες δεν είναι διαθέσιμες.');
        const value:unknown=await response.json();
        if(!value || typeof value!=='object' || Array.isArray(value))
          throw new Error('Μη επαληθευμένη απάντηση τοποθεσιών.');
        const data=value as Record<string,unknown>;
        if(data.organization_id!=='lefteria_fc_academy' ||
          !Array.isArray(data.cities)||!Array.isArray(data.locations)||
          !Array.isArray(data.scopes)||data.locations.length>3000)
          throw new Error('Μη επαληθευμένος κατάλογος τοποθεσιών.');
        const cities=new Map<string,string>();
        for(const row of data.cities as Record<string,unknown>[]){
          if(typeof row.id==='string' && typeof row.name==='string' &&
            row.is_active===true)cities.set(row.id,row.name);
        }
        const scopedCities=new Set((data.scopes as Record<string,unknown>[])
          .filter(s=>s.organization_id==='lefteria_fc_academy' &&
            typeof s.academy_city_id==='string')
          .map(s=>s.academy_city_id as string));
        const writableCities=new Set((data.scopes as Record<string,unknown>[])
          .filter(scope=>scope.organization_id==='lefteria_fc_academy' &&
            scope.can_write_catalog===true && typeof scope.academy_city_id==='string')
          .map(scope=>scope.academy_city_id as string));
        const authorizedCities:CreationCity[]=[...cities]
          .filter(([id])=>scopedCities.has(id))
          .map(([id,name])=>({id,name,canCreateLocation:writableCities.has(id)}));
        const options:CreationLocation[]=[];
        for(const row of data.locations as Record<string,unknown>[]){
          if(typeof row.id!=='string' || !/^[0-9a-f-]{36}$/i.test(row.id) ||
            typeof row.city_id!=='string' || typeof row.name!=='string' ||
            row.is_active!==true || row.can_manage!==true ||
            !cities.has(row.city_id) || !scopedCities.has(row.city_id))continue;
          options.push({id:row.id,city_id:row.city_id,
            label:cities.get(row.city_id)+' · '+row.name});
        }
        if(controller.signal.aborted || organization!==owner ||
          $auth.session?.user.id!==actor)return;
        creationCities=authorizedCities;
        creationLocations=options;
        const newlyCreatedId=pendingLocationId;
        const requested=newlyCreatedId||page.url.searchParams.get('location_id')||'';
        const requestedSite=options.find(l=>l.id===requested);
        const preferred=requestedSite ??
          (!newlyCreatedId && options.length===1?options[0]:null);
        if(preferred)creationCityId=preferred.city_id;
        else if(!authorizedCities.some(c=>c.id===creationCityId))
          creationCityId=authorizedCities.length===1?authorizedCities[0].id:'';
        creationLocationId=preferred?.id ?? '';
        if(newlyCreatedId && !requestedSite)creationLocationsError=
          'Η τοποθεσία αποθηκεύτηκε, αλλά δεν εμφανίζεται στον επαληθευμένο κατάλογο. Ελέγξτε τα δικαιώματα και ανανεώστε πριν δημιουργήσετε ομάδα.';
        else pendingLocationId='';
        if(!authorizedCities.length)creationLocationsError=
          'Δεν υπάρχουν εξουσιοδοτημένες ενεργές πόλεις.';
      }catch{
        if(!controller.signal.aborted)creationLocationsError=
          'Δεν επιβεβαιώθηκαν οι εξουσιοδοτημένες τοποθεσίες. Ανανέωση και νέα προσπάθεια.';
      }finally{if(!controller.signal.aborted)creationLocationsLoading=false;}
    })();
    return ()=>controller.abort();
  });

  async function createInlineLocation() {
    if(!browser || !canCreate || organization!=='lefteria_fc_academy' ||
      !mayCreateLocation ||
      locationSaving || saving || creationLocationsLoading)return;
    const name=locationName.trim(), slug=locationSlug.trim().toLowerCase();
    if(!name || name.length>120 || /[\x00-\x1f\x7f]/.test(name) ||
      !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(slug) || slug.length>120 ||
      locationAddress.length>300 || /[\x00-\x1f\x7f]/.test(locationAddress) ||
      locationMapsUrl.length>2048 ||
      (locationMapsUrl && !/^https:\/\/(?:www\.)?(?:google\.[a-z.]+\/maps|maps\.app\.goo\.gl\/)/i.test(locationMapsUrl))){
      locationCreateError='Συμπληρώστε έγκυρο όνομα, slug και προαιρετικό Google Maps HTTPS URL.';
      return;
    }
    const city=creationCityId, actor=$auth.session?.user.id;
    locationSaving=true;locationCreateError='';
    try {
      const response=await fetch('/api/admin/football/territories?organization_id=lefteria_fc_academy',{
        method:'POST',credentials:'same-origin',cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({kind:'location',data:{
          city_id:city,name,slug,address:locationAddress,
          google_maps_url:locationMapsUrl}})
      });
      if(actor!==$auth.session?.user.id || organization!=='lefteria_fc_academy')return;
      if(response.status===401||response.status===403){
        void auth.bootstrap();
        throw new Error('Δεν επιτρέπεται η δημιουργία τοποθεσίας με τα τρέχοντα δικαιώματα.');
      }
      if(!response.ok)throw new Error(response.status===409
        ?'Η τοποθεσία υπάρχει ήδη ή άλλαξαν τα στοιχεία της πόλης.'
        :'Η τοποθεσία δεν αποθηκεύτηκε. Ελέγξτε τα στοιχεία και τα δικαιώματά σας.');
      const raw:unknown=await response.json();
      if(!raw || typeof raw!=='object' || Array.isArray(raw) ||
        (raw as Record<string,unknown>).kind!=='location' ||
        (raw as Record<string,unknown>).organization_id!=='lefteria_fc_academy' ||
        typeof (raw as Record<string,unknown>).id!=='string' ||
        !/^[0-9a-f-]{36}$/i.test((raw as Record<string,unknown>).id as string))
        throw new Error('Η νέα τοποθεσία δεν επαληθεύτηκε. Ανανεώστε τον κατάλογο.');
      pendingLocationId=(raw as Record<string,unknown>).id as string;
      showInlineLocation=false;
      locationName='';locationSlug='';locationAddress='';locationMapsUrl='';
      createRequestId='';
      locationReload++;
    }catch(error){
      locationCreateError=error instanceof Error?error.message:'Αποτυχία δημιουργίας τοποθεσίας.';
    }finally{locationSaving=false;}
  }

  async function createGroup() {
    if (!browser || !canCreate || !organization || !directory || loading ||
        saving || !showCreate) return;
    const name = groupName;
    if (!name || name.length > 120 || name !== name.trim() ||
        /[\x00-\x1f\x7f]/.test(name)) {
      createError = 'Εισάγετε όνομα τμήματος χωρίς περιττά κενά (έως 120 χαρακτήρες).';
      return;
    }
    const selected = organization;
    const actor = $auth.session?.user.id;
    const selectedLocation=selected==='lefteria_fc_academy'
      ?creationLocations.find(item=>item.id===creationLocationId)??null:null;
    if(selected==='lefteria_fc_academy' && (!selectedLocation ||
      selectedLocation.city_id!==creationCityId || creationLocationsLoading || locationSaving)){
      createError='Επιλέξτε μία επαληθευμένη πόλη και τοποθεσία πριν από τη δημιουργία.';
      return;
    }
    const safeText = (value:string,max:number)=>
      value.length<=max && !/[\x00-\x1f\x7f]/.test(value);
    const bannerValid = (value:string)=>!value ||
      /^https:\/\/[A-Za-z0-9.-]+(?:\/[A-Za-z0-9_.\/%?&=-]*)?$/.test(value);
        if (selected==='lefteria_fc' ?
        (!safeText(teamLevel,100)||!teamLevel.trim()||teamLevel!==teamLevel.trim()||
          !safeText(teamDescription,2000)||!safeText(teamBanner,2048)||
          !bannerValid(teamBanner)) :
        (![academyAge,academyCoach,academySchedule,academyDescription,
          academySeason,academyBanner].every((v,i)=>safeText(v,
            [80,120,500,2000,40,2048][i])) ||
          !Number.isSafeInteger(academyCapacity)||academyCapacity<1||academyCapacity>200||
          !Number.isSafeInteger(academyOrder)||academyOrder<0||academyOrder>10000||
          !bannerValid(academyBanner))) {
      createError='Ελέγξτε τα στοιχεία, τα μήκη και τη διεύθυνση HTTPS του τμήματος.';
      return;
    }
    const requestId=createRequestId||crypto.randomUUID();
    createRequestId=requestId;
    const profile=selected==='lefteria_fc'
      ?{team_profile:{level:teamLevel,description:teamDescription,banner_url:teamBanner}}
      :{academy_profile:{age_range:academyAge,coach_name:academyCoach,
        training_schedule:academySchedule,description:academyDescription,
        max_players:academyCapacity,season:academySeason,
        banner_url:academyBanner,display_order:academyOrder}};
    saving = true;
    createError = null;
    try {
      const query = new URLSearchParams({organization_id:selected});
      const response = await fetch('/api/admin/football/groups/with-profile?' + query.toString(), {
        method:'POST', credentials:'same-origin', cache:'no-store',
        headers:{'Content-Type':'application/json','X-Lefteria-Admin':'1'},
        body:JSON.stringify({name,request_id:requestId,...profile,
          ...(selectedLocation?{location_id:selectedLocation.id}:{})})
      });
      if (organization !== selected || $auth.session?.user.id !== actor || !canCreate) return;
      if (response.status === 401 || response.status === 403) {
        void auth.bootstrap();
        showCreate = false;
        throw new Error('Τα δικαιώματα ή η σύνδεσή σας άλλαξαν.');
      }
      if (response.status === 409) {
        showCreate = false;
        retry++;
        throw new Error('Το τμήμα άλλαξε ή υπάρχει σύγκρουση. Ανανέωση καταλόγου.');
      }
      if (!response.ok) throw new Error('Το τμήμα δεν δημιουργήθηκε. Ελέγξτε το όνομα.');
      const saved: unknown = await response.json();
      if (!saved || typeof saved !== 'object' || Array.isArray(saved))
        throw new Error('Μη επαληθευμένη απάντηση αποθήκευσης.');
      const value = saved as Record<string,unknown>;
      if (typeof value.id !== 'string' || !/^grp_[0-9a-f]{32}$/.test(value.id) ||
          value.name !== name || value.kind !==
            (selected === 'lefteria_fc' ? 'team' : 'academy_group') ||
          value.active_players !== 0 ||
          !value.profile || typeof value.profile !== 'object' ||
          Array.isArray(value.profile) ||
          (!Number.isSafeInteger((value.profile as Record<string,unknown>).revision) ||
           ((value.profile as Record<string,unknown>).revision as number) < 1) ||
          (selected === 'lefteria_fc'
            ? (value.profile as Record<string,unknown>).team_id !== value.id
            : (value.profile as Record<string,unknown>).group_id !== value.id) ||
          (selectedLocation && (value.location_id!==selectedLocation.id ||
            value.city_id!==selectedLocation.city_id)))
        throw new Error('Μη επαληθευμένη πλήρης καταχώριση τμήματος.');
      lastCreatedGroupId=value.id;
      lastCreatedOrganization=selected;
      lastCreatedLocation=selectedLocation;
      focusedGroupId=value.id;
      showCreate = false;
      groupName = '';
      createRequestId='';
      retry++;
    } catch(error) {
      if (organization === selected && $auth.session?.user.id === actor)
        createError = error instanceof Error ? error.message : 'Η δημιουργία δεν ολοκληρώθηκε.';
    } finally { saving = false; }
  }


  function verifiedDirectory(value: unknown, expected: Organization): value is Directory {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
    const response = value as Record<string, unknown>;
    const groupKind = expected === 'lefteria_fc' ? 'team' : 'academy_group';
    const uniqueIds = new Set<string>();
    return response.organization_id === expected && response.ready === true &&
      Number.isSafeInteger(response.unassigned_players) && (response.unassigned_players as number) >= 0 &&
      Array.isArray(response.groups) && response.groups.length <= 150 &&
      response.groups.every((group: unknown) => {
        if (!group || typeof group !== 'object' || Array.isArray(group)) return false;
        const row = group as Record<string, unknown>;
        if (typeof row.id !== 'string' || row.id.length < 1 ||
            row.id.length > 128 || uniqueIds.has(row.id)) return false;
        uniqueIds.add(row.id);
        return typeof row.name === 'string' && row.name.length > 0 && row.name.length <= 200 &&
          row.kind === groupKind && Number.isSafeInteger(row.active_players) &&
          (row.active_players as number) >= 0;
      });
  }

  $effect(() => {
    if (browser && $auth.status === 'anonymous') void goto('/admin/login', {replace:true});
  });
  $effect(() => {
    const selected = organization;
    const allowed = canCreate;
    void selected; void allowed;
    showCreate = false;
    groupName = '';
    createRequestId='';
    teamLevel="Α' Ομάδα";teamDescription='';teamBanner='';
    academyAge='';academyCoach='';academySchedule='';
    academyDescription='';academyCapacity=25;academySeason='';
    academyBanner='';academyOrder=0;
    createError = null;
  });
  // Deep links from the work dashboard open the *real* creation form.
  // These never grant permission: the API rechecks every write.
  $effect(()=>{
    const selected=organization,loaded=directory?.organization_id===selected;
    const wantsNew=page.url.searchParams.get('new')==='1';
    if(!browser||openedRequestedCreate||!wantsNew||!loaded||!canCreate)return;
    openedRequestedCreate=true;
    showCreate=true;
  });

  $effect(() => {
    // An owner revoked by the server may never remain selected in the UI.
    if (!assignments.some(org => org.id === organization))
      organization = scopedAdminOrganization($auth.session?.user ?? null, assignments.map(org => org.id));
  });
  $effect(() => {
    const sessionId = $auth.session?.user.id;
    const role = $auth.session?.user.role;
    const permissions = $auth.session?.user.permissions.join('|');
    const grants = assignments.map(org => org.id).join('|');
    const owner = organization;
    const generation = retry;
    void sessionId; void role; void permissions; void grants; void generation;

    directory = null;
    loading = false;
    unavailable = false;
    if (!browser || $auth.status !== 'authenticated' || !$auth.session || !owner ||
        !assignments.some(org => org.id === owner)) return;

    const controller = new AbortController();
    loading = true;
    void (async () => {
      try {
        const query = new URLSearchParams({organization_id:owner});
        const response = await fetch('/api/admin/football/audiences?' + query.toString(), {
          method:'GET', credentials:'same-origin', cache:'no-store', signal:controller.signal
        });
        if (controller.signal.aborted) return;
        if (response.status === 401) {
          auth.invalidateSession();
          return;
        }
        if (response.status === 403) {
          void auth.bootstrap();
          throw new Error('Audience permissions were revoked.');
        }
        if (!response.ok) throw new Error('Roster is unavailable.');
        const raw:unknown = await response.json();
        if (!verifiedDirectory(raw,owner)) throw new Error('Unverified roster audience.');
        if (!controller.signal.aborted) directory = raw;
      } catch {
        if (!controller.signal.aborted) unavailable = true;
      } finally { if (!controller.signal.aborted) loading = false; }
    })();
    return () => controller.abort();
  });
  $effect(()=>{
    const actor=$auth.session?.user.id;
    const role=$auth.session?.user.role;
    const permissions=$auth.session?.user.permissions.join('|');
    const grants=assignments.map(org=>org.id).join('|');
    const selected=otherOwner;
    void role;void permissions;void grants;void retry;
    otherDirectory=null;otherLoading=false;otherUnavailable=false;
    if(!browser||$auth.status!=='authenticated'||!actor||!selected||assignments.length<2)return;
    const controller=new AbortController();otherLoading=true;
    void (async()=>{
      try{
        const query=new URLSearchParams({organization_id:selected});
        const response=await fetch('/api/admin/football/audiences?'+query.toString(),{
          credentials:'same-origin',cache:'no-store',signal:controller.signal
        });
        if(controller.signal.aborted)return;
        if(response.status===401){auth.invalidateSession();return;}
        if(response.status===403){void auth.bootstrap();throw new Error('Revoked roster access');}
        if(!response.ok)throw new Error('Roster unavailable');
        const raw:unknown=await response.json();
        if(!verifiedDirectory(raw,selected))throw new Error('Unverified roster');
        if(!controller.signal.aborted && $auth.session?.user.id===actor && otherOwner===selected)otherDirectory=raw;
      }catch{if(!controller.signal.aborted)otherUnavailable=true;}
      finally{if(!controller.signal.aborted)otherLoading=false;}
    })();
    return ()=>controller.abort();
  });
</script>

<svelte:head><title>Ομάδες & τμήματα | Διαχείριση Λευτεριά FC</title></svelte:head>

{#if $auth.status === 'authenticated' && $auth.session}
  <AdminFrame user={$auth.session.user} active="groups" workspaceOwner={organization} playerView={directoryView==='players'} busy={$auth.busy} onLogout={() => void auth.logout()}>
    {#if assignments.length}
      <div class="admin-audiences" data-testid="admin-live-audiences">
        <p class="admin-eyebrow">ΑΓΩΝΙΣΤΙΚΟ · ΑΚΑΔΗΜΙΑ · ΠΡΑΓΜΑΤΙΚΑ ΜΕΛΗ</p>
        <h1>{directoryView==='players'?'CRM · Παίκτες & προφίλ':'Ομάδες & τμήματα'}</h1>
        {#if directoryView==='players' && organization}<AdminCrmWorkspaceNav user={$auth.session.user} active={organization==='lefteria_fc'?'first_team_players':'academy_players'}/>{/if}
        <p class="admin-muted">Ζωντανή εικόνα των ομάδων και των τμημάτων από το επαληθευμένο ρόστερ, όχι διπλές στατικές καταχωρίσεις. Οι αριθμοί περιλαμβάνουν μόνο ενεργούς παίκτες χωρίς αρχειοθέτηση ή μεταγραφή.</p>
        {#if assignments.length>1}
          <section class="admin-panel" aria-labelledby="combined-football-heading" data-testid="admin-combined-football">
            <h2 id="combined-football-heading">Πρώτη ομάδα & Ακαδημία</h2>
            <p class="admin-muted">Όλες οι εξουσιοδοτημένες ομάδες σε μία οθόνη. Επιλέξτε τμήμα για τη διαχείρισή του.</p>
            <div class="admin-combined-grid">
              {#each combinedDirectories as item (item.owner)}
                <section class="admin-combined-owner" aria-label={item.name}>
                  <h3>{item.name}</h3>
                  {#if item.directory?.organization_id===item.owner}
                    <p class="admin-muted">{item.directory.groups.length} ομάδες / τμήματα · {item.directory.groups.reduce((n,g)=>n+g.active_players,0)} συμμετοχές</p>
                    <ul>{#each item.directory.groups as group (group.id)}
                      <li><a data-sveltekit-reload href={`/admin/groups?organization_id=${encodeURIComponent(item.owner)}&group_id=${encodeURIComponent(group.id)}#admin-group-workspace`}>{group.name}</a><span>{group.active_players} παίκτες</span></li>
                    {/each}</ul>
                    {#if !item.directory.groups.length}<p class="admin-muted">Δεν υπάρχουν ορατές ομάδες.</p>{/if}
                  {:else if item.unavailable}<p role="status">Ο κατάλογος δεν είναι διαθέσιμος.</p>
                  {:else}<p role="status">Φόρτωση καταλόγου…</p>{/if}
                  <a data-sveltekit-reload href={`/admin/groups?organization_id=${encodeURIComponent(item.owner)}`}>Διαχείριση ομάδων ›</a>
                  {#if ['manager','super_admin'].includes($auth.session.user.role) && hasAdminPermission($auth.session.user.permissions,item.owner==='lefteria_fc'?'teams:write':'academy:write') && (item.owner!=='lefteria_fc'||$auth.session.user.role==='super_admin')}
                    <a data-sveltekit-reload href={`/admin/groups?organization_id=${encodeURIComponent(item.owner)}&view=players`}>Παίκτες & προφίλ ›</a>
                  {/if}
                </section>
              {/each}
            </div>
          </section>
        {/if}
        <section class="admin-panel admin-audiences-select" aria-label="Οργανισμός ομάδων">
          {#if assignments.length > 1}
            <label class="admin-groups-organization-choice">Πεδίο επεξεργασίας
              <select value={organization ?? ''} data-testid="admin-groups-organization"
                disabled={loading || saving}
                onchange={event => {
                  const next = event.currentTarget.value;
                  if (assignments.some(org => org.id === next)) organization = next as Organization;
                }}>
                {#each assignments as available (available.id)}
                  <option value={available.id}>{available.name}</option>
                {/each}
              </select>
            </label>
          {:else}
            <span class="admin-organization-context-label" data-testid="admin-audience-owner">
              {assignments[0]?.name ?? 'Δεν υπάρχει εξουσιοδοτημένος οργανισμός'}
            </span>
          {/if}
          <button type="button" class="admin-secondary" onclick={() => retry++} disabled={loading}>Ανανέωση</button>
        </section>

        {#if loading}
          <section class="admin-panel" role="status" data-testid="admin-audiences-loading">Φόρτωση πραγματικών τμημάτων…</section>
        {:else if unavailable || !directory}
          <section class="admin-panel" role="alert" data-testid="admin-audiences-unavailable">
            <p>Ο κατάλογος τμημάτων δεν είναι διαθέσιμος. Δεν εμφανίζουμε υποθετικές ομάδες ή στοιχεία παιδιών.</p>
            <button type="button" class="admin-secondary" onclick={() => retry++}>Δοκιμή ξανά</button>
          </section>
        {:else}
          <div class="admin-audiences-stats" aria-label="Σύνοψη εξουσιοδοτημένων ομάδων">
            <div><span>Ορατές ομάδες / τμήματα</span><strong>{directory.groups.length}</strong></div>
            <div><span>Συμμετοχές σε ορατά τμήματα</span><strong>{directory.groups.reduce((n,g) => n+g.active_players,0)}</strong></div>
            {#if $auth.session?.user.role==='super_admin'}
              <div data-testid="admin-audiences-unassigned"><span>Χωρίς τμήμα (οργανισμός)</span><strong>{directory.unassigned_players}</strong></div>
            {:else}
              <div data-testid="admin-audiences-scoped"><span>Πεδίο πρόσβασης</span><strong class="admin-audiences-scope-text">Μόνο ανατεθειμένα τμήματα</strong></div>
            {/if}
          </div>
          {#if canCreate}
            <div class="admin-directory-switch" role="group" aria-label="Ενότητες διαχείρισης">
              <button type="button" aria-pressed={directoryView==='groups'}
                data-testid="admin-directory-teams"
                onclick={()=>{if(requestedPlayerId)void goto('/admin/groups?organization_id=lefteria_fc_academy');else directoryView='groups';}}>Ομάδες & τμήματα</button>
              <button type="button" aria-pressed={directoryView==='players'}
                data-testid="admin-directory-players"
                onclick={()=>directoryView='players'}>Παίκτες & προφίλ</button>
            </div>
          {/if}
          {#if canCreate && directoryView==='players' && $auth.session}
            <AdminRosterPlayersPanel owner={directory.organization_id} actorId={$auth.session.user.id}
              groups={directory.groups} {requestedPlayerId} allowAssignments={requestedPlayerId===null} allowPrivate={true} allowEvaluationWrite={Boolean(canCreate)}
              allowGuardianReview={organization==='lefteria_fc_academy'&&$auth.session.user.role==='super_admin'&&Boolean(canCreate)}
              allowFinance={organization==='lefteria_fc_academy' &&
                $auth.session.user.role==='super_admin' &&
                hasAdminPermission($auth.session.user.permissions,'finance:read')}
              onExpired={()=>auth.invalidateSession()}
              onRevoked={()=>void auth.bootstrap()} onChanged={()=>retry++}/>
          {/if}
          {#if directoryView==='groups'}
          {#if lastCreatedGroupId && lastCreatedOrganization===organization}
            <section class="admin-panel admin-group-create-next" data-testid="admin-group-next-setup" role="status">
              <p class="admin-eyebrow">ΤΟ ΤΜΗΜΑ ΑΠΟΘΗΚΕΥΤΗΚΕ</p>
              {#if organization==='lefteria_fc_academy'}
                <h2>Τμήμα ενεργό στη σωστή τοποθεσία</h2>
                <p>Η δημιουργία και η ανάθεση στην τοποθεσία {lastCreatedLocation?.label ?? ''}
                  ολοκληρώθηκαν με την ίδια επιβεβαιωμένη συναλλαγή. Οι αντίπαλοι
                  και τα γήπεδα αγώνων παραμένουν κοινά ανά πόλη.</p>
                <a class="admin-group-next-link"
                  href={`/admin/territories?organization_id=lefteria_fc_academy&city_id=${encodeURIComponent(lastCreatedLocation?.city_id??'')}&location_id=${encodeURIComponent(lastCreatedLocation?.id??'')}#group-placement`}
                  data-testid="admin-verified-group-location">Διαχείριση τοποθεσίας ↗</a>
              {:else}
                <h2>Οργάνωση ομάδας</h2>
                <p>Η ομάδα δημιουργήθηκε. Μπορείτε να της αναθέσετε το δικό της
                  αγωνιστικό πεδίο, αντιπάλους και γήπεδα χωρίς πρόσβαση στα στοιχεία Ακαδημίας.</p>
                <a class="admin-group-next-link"
                  href={`/admin/territories?organization_id=lefteria_fc&team_id=${encodeURIComponent(lastCreatedGroupId)}#team-heading`}
                  data-testid="admin-assign-team-scope">Ρύθμιση γηπέδων & αντιπάλων ↗</a>
              {/if}
            </section>
          {/if}
          {#if canCreate}
            <section class="admin-panel admin-group-create" data-testid="admin-group-create" aria-labelledby="admin-group-create-heading">
              <div class="admin-group-create-heading">
                <div>
                  <p class="admin-eyebrow">ΔΗΜΙΟΥΡΓΙΑ · ΕΛΕΓΧΟΣ ΔΙΚΑΙΩΜΑΤΩΝ</p>
                  <h2 id="admin-group-create-heading">Νέο {organization === 'lefteria_fc' ? 'τμήμα / ομάδα' : 'τμήμα Ακαδημίας'}</h2>
                </div>
                {#if !showCreate}
                  <button type="button" class="admin-secondary" onclick={() => {showCreate=true;createError=null;}} disabled={saving}>Προσθήκη τμήματος</button>
                {/if}
              </div>
              {#if showCreate}
                <form class="admin-group-create-form" data-testid="admin-group-create-form"
                  onsubmit={event => {event.preventDefault(); void createGroup();}}>
                  <label for="admin-group-name">Όνομα τμήματος</label>
                  <input id="admin-group-name" type="text" required maxlength="120"
                    bind:value={groupName} disabled={saving}
                    oninput={() => createRequestId=''}/>
                  {#if organization === 'lefteria_fc'}
                    <label>Επίπεδο ομάδας
                      <input type="text" maxlength="100" required list="admin-team-level-options" bind:value={teamLevel}
                        disabled={saving} oninput={() => createRequestId=''}/>
                    </label>
                    <datalist id="admin-team-level-options">
                      <option value="Α' Ομάδα"></option>
                      <option value="Γυναικεία Ομάδα"></option>
                      <option value="Επιλεγμένη Ομάδα"></option>
                      <option value="Β' Ομάδα"></option>
                      <option value="Αναπτυξιακή Ομάδα"></option>
                    </datalist>
                    <label>Περιγραφή ομάδας
                      <textarea rows="3" maxlength="2000" bind:value={teamDescription}
                        disabled={saving} oninput={() => createRequestId=''}/>
                    </label>
                    <label>Banner (HTTPS)
                      <input type="url" maxlength="2048" bind:value={teamBanner}
                        disabled={saving} placeholder="https://..."
                        oninput={() => createRequestId=''}/>
                    </label>
                  {:else}
                    <div class="admin-academy-placement">
                      <label>1. Πόλη Ακαδημίας *
                        <select bind:value={creationCityId}
                          disabled={saving || locationSaving || creationLocationsLoading}
                          required data-testid="admin-group-city"
                          onchange={() => {creationLocationId='';createRequestId='';showInlineLocation=false;}}>
                          <option value="">Επιλέξτε πόλη</option>
                          {#each creationCities as city (city.id)}
                            <option value={city.id}>{city.name}</option>
                          {/each}
                        </select>
                      </label>
                      {#if $auth.session?.user.role==='super_admin'}
                        <a class="admin-add-city-link"
                          href="/admin/territories?organization_id=lefteria_fc_academy#create-city"
                          data-testid="admin-new-city-route">+ Δημιουργία πόλης</a>
                      {/if}
                      <label>2. Τοποθεσία Ακαδημίας *
                        <select bind:value={creationLocationId}
                          disabled={saving || locationSaving || creationLocationsLoading || !creationCityId}
                          required data-testid="admin-group-verified-location"
                          onchange={() => createRequestId=''}>
                          <option value="">Επιλέξτε τοποθεσία</option>
                          {#each cityLocations as site (site.id)}
                            <option value={site.id}>{site.label}</option>
                          {/each}
                        </select>
                      </label>
                      <button type="button" class="admin-secondary admin-location-toggle"
                        data-testid="admin-group-add-location"
                        disabled={saving || locationSaving || !mayCreateLocation}
                        onclick={() => {showInlineLocation=!showInlineLocation;locationCreateError='';}}>
                        {showInlineLocation?'Κλείσιμο δημιουργίας':'+ Νέα τοποθεσία'}
                      </button>
                      {#if creationCityId && !mayCreateLocation}
                        <p class="admin-muted admin-location-note">Μπορείτε να επιλέξετε εξουσιοδοτημένη τοποθεσία, αλλά δεν έχετε δικαίωμα δημιουργίας νέας σε αυτή την πόλη.</p>
                      {/if}
                      {#if showInlineLocation && mayCreateLocation}
                        <div class="admin-inline-location" data-testid="admin-group-inline-location">
                          <h3>Νέα τοποθεσία στην επιλεγμένη πόλη</h3>
                          <p class="admin-muted">Η ομάδα δεν θα αποθηκευτεί μέχρι να επιλέξετε επαληθευμένη τοποθεσία.</p>
                          <label>Όνομα τοποθεσίας *
                            <input bind:value={locationName} maxlength="120" disabled={locationSaving || saving}/>
                          </label>
                          <label>URL slug *
                            <input bind:value={locationSlug} maxlength="120" placeholder="pafos-centre"
                              disabled={locationSaving || saving}/>
                          </label>
                          <label>Διεύθυνση
                            <input bind:value={locationAddress} maxlength="300" disabled={locationSaving || saving}/>
                          </label>
                          <label>Google Maps URL (HTTPS)
                            <input type="url" bind:value={locationMapsUrl} maxlength="2048"
                              placeholder="https://maps.app.goo.gl/..."
                              disabled={locationSaving || saving}/>
                          </label>
                          {#if locationCreateError}
                            <p role="alert" class="admin-group-error">{locationCreateError}</p>
                          {/if}
                          <button type="button" class="admin-secondary" disabled={locationSaving || saving}
                            onclick={() => void createInlineLocation()}>
                            {locationSaving?'Αποθήκευση τοποθεσίας…':'Δημιουργία & επιλογή τοποθεσίας'}
                          </button>
                        </div>
                      {/if}
                    </div>
                    {#if creationLocationsLoading}
                      <p role="status" class="admin-muted">Επαλήθευση τοποθεσιών…</p>
                    {:else if creationLocationsError}
                      <p role="alert" class="admin-group-error">{creationLocationsError}</p>
                    {/if}
                    <label>Ηλικιακή ομάδα
                      <input type="text" maxlength="80" bind:value={academyAge}
                        disabled={saving} oninput={() => createRequestId=''}/>
                    </label>
                    <label>Προπονητής (όνομα εμφάνισης)
                      <input type="text" maxlength="120" bind:value={academyCoach}
                        disabled={saving} oninput={() => createRequestId=''}/>
                    </label>
                    <label>Πρόγραμμα προπονήσεων
                      <textarea rows="3" maxlength="500" bind:value={academySchedule}
                        disabled={saving} oninput={() => createRequestId=''}/>
                    </label>
                    <label>Περιγραφή τμήματος
                      <textarea rows="3" maxlength="2000" bind:value={academyDescription}
                        disabled={saving} oninput={() => createRequestId=''}/>
                    </label>
                    <label>Χωρητικότητα
                      <input type="number" min="1" max="200" step="1" required
                        bind:value={academyCapacity} disabled={saving}
                        oninput={() => createRequestId=''}/>
                    </label>
                    <label>Αγωνιστική περίοδος
                      <input type="text" maxlength="40" bind:value={academySeason}
                        disabled={saving} oninput={() => createRequestId=''}/>
                    </label>
                    <label>Banner (HTTPS)
                      <input type="url" maxlength="2048" bind:value={academyBanner}
                        disabled={saving} placeholder="https://..."
                        oninput={() => createRequestId=''}/>
                    </label>
                    <label>Σειρά εμφάνισης
                      <input type="number" min="0" max="10000" step="1" required
                        bind:value={academyOrder} disabled={saving}
                        oninput={() => createRequestId=''}/>
                    </label>
                  {/if}
                  <div class="admin-group-create-actions">
                    <button type="submit" class="admin-secondary"
                      disabled={saving || (organization==='lefteria_fc_academy' &&
                        (!creationCityId || !creationLocationId || locationSaving || creationLocationsLoading || !!creationLocationsError))}>
                      {saving ? 'Δημιουργία…' : 'Δημιουργία'}</button>
                    <button type="button" class="admin-secondary" disabled={saving}
                      onclick={() => {showCreate=false;createError=null;}}>Ακύρωση</button>
                  </div>
                </form>
              {/if}
              {#if createError}
                <p role="alert" class="admin-group-error">{createError}</p>
              {/if}
            </section>
          {/if}
          {#if !focusedGroup}
          <section class="admin-panel" aria-labelledby="admin-audiences-title">
            <div class="admin-audiences-heading">
              <h2 id="admin-audiences-title">{organization === 'lefteria_fc' ? 'Ομάδες πρώτης ομάδας' : 'Τμήματα Ακαδημίας'}</h2>
              <span class="admin-audiences-caption">Ενημέρωση από το ρόστερ</span>
            </div>
            {#if directory.groups.length>0}
              <label class="admin-audience-search" for="admin-group-search">
                Αναζήτηση ομάδας / τμήματος
                <input id="admin-group-search" type="search" bind:value={groupSearch}
                  placeholder="Όνομα ομάδας ή ηλικιακού τμήματος…" autocomplete="off"
                  data-testid="admin-group-search"/>
              </label>
              {#if organization==='lefteria_fc_academy' && placementsReady}
                <div class="admin-group-hierarchy-filters" data-testid="academy-city-location-hierarchy">
                  <label>Πόλη
                    <select value={cityFilter} onchange={event=>{cityFilter=event.currentTarget.value;siteFilter='';}}>
                      <option value="">Όλες οι εξουσιοδοτημένες πόλεις</option>
                      {#each placementCities as [id,label] (id)}
                        <option value={id}>{label}</option>
                      {/each}
                    </select>
                  </label>
                  <label>Τοποθεσία
                    <select value={siteFilter} onchange={event=>siteFilter=event.currentTarget.value}>
                      <option value="">Όλες οι τοποθεσίες</option>
                      {#each placementSites as [id,label] (id)}
                        <option value={id}>{label}</option>
                      {/each}
                    </select>
                  </label>
                  <a href="/admin/territories?organization_id=lefteria_fc_academy"
                    data-testid="academy-shared-city-directory">Πόλεις, εγκαταστάσεις & κοινά γήπεδα/αντίπαλοι</a>
                </div>
              {/if}
            {/if}
            {#if directory.groups.length === 0}
              {#if $auth.session.user.role==='super_admin'}
                <p class="admin-muted" data-testid="admin-audiences-empty">
                  Δεν υπάρχουν τμήματα στο επαληθευμένο ρόστερ αυτού του οργανισμού.
                  Η εποπτεία Super Admin περιλαμβάνει και τμήματα χωρίς ανάθεση.
                </p>
                {#if canCreate}
                  <button type="button" class="admin-secondary"
                    onclick={()=>{showCreate=true;}} data-testid="admin-first-group-button">
                    Δημιουργία πρώτου τμήματος
                  </button>
                {/if}
              {:else}
                <section class="admin-panel admin-scoped-empty" role="status"
                  data-testid="admin-audiences-unassigned-access">
                  <h3>Δεν εμφανίζονται ανατεθειμένα τμήματα</h3>
                  <p class="admin-muted">
                    Η λίστα εμφανίζει μόνο ομάδες και τμήματα στα οποία έχετε ενεργή
                    εξουσιοδότηση. Δεν σημαίνει ότι ο οργανισμός δεν έχει ομάδες.
                    Αν αναμένετε να βλέπετε κάποιο τμήμα, ζητήστε από τον Super Admin
                    να ελέγξει την ανάθεση πόλης, τοποθεσίας ή Πρώτης Ομάδας.
                  </p>
                  <a class="admin-secondary" data-testid="admin-audiences-verify-scope"
                    href={`/admin/territories?organization_id=${encodeURIComponent(organization??'')}`}>
                    Έλεγχος των εξουσιοδοτημένων τοποθεσιών
                  </a>
                  {#if canCreate && organization==='lefteria_fc_academy'}
                    <p class="admin-muted">
                      Για νέο τμήμα Ακαδημίας απαιτείται ενεργή, εξουσιοδοτημένη τοποθεσία.
                      Η δημιουργία επιβεβαιώνει την άδεια ξανά στη βάση δεδομένων.
                    </p>
                    <button type="button" class="admin-secondary"
                      onclick={()=>{showCreate=true;}} data-testid="admin-first-group-button">
                      Προσθήκη τμήματος στην τοποθεσία μου
                    </button>
                  {/if}
                </section>
              {/if}
            {:else}
              <ul class="admin-audiences-list" data-testid="admin-audiences-groups">
                {#each visibleGroups as group,i (group.id)}
                  {#if organization==='lefteria_fc_academy' && groupPlacements[group.id] &&
                    (i===0||groupPlacements[visibleGroups[i-1].id]?.locationId!==groupPlacements[group.id].locationId)}
                    <li class="admin-group-location-heading" data-testid="academy-group-location-heading">
                      <strong>{groupPlacements[group.id].cityName}</strong>
                      <span>{groupPlacements[group.id].locationName}</span>
                    </li>
                  {/if}
                  <li class:admin-audience-selected={focusedGroupId===group.id}>
                    <div class="admin-audience-card-title">
                      <span class="admin-audiences-type">{group.kind === 'team' ? 'ΟΜΑΔΑ' : 'ΤΜΗΜΑ ΑΚΑΔΗΜΙΑΣ'}</span>
                      <button type="button" class="admin-group-name-link" onclick={()=>focusedGroupId=group.id}>{group.name}</button>
                      {#if organization==='lefteria_fc_academy'}
                        <span class="admin-group-placement" data-testid="academy-group-exact-placement">
                          {groupPlacements[group.id]
                            ? groupPlacements[group.id].cityName+' / '+groupPlacements[group.id].locationName
                            : 'Επίσημη τοποθεσία: μη επαληθευμένη'}
                        </span>
                      {/if}
                    </div>
                    {#if organization==='lefteria_fc_academy' && academyCardProfiles[group.id]}
                      <div class="admin-audience-card-meta" data-testid="admin-academy-card-profile">
                        {#if academyCardProfiles[group.id].age_range}
                          <span class="admin-audience-age">{academyCardProfiles[group.id].age_range}</span>
                        {/if}
                        {#if academyCardProfiles[group.id].coach_name}
                          <span>Προπονητής: {academyCardProfiles[group.id].coach_name}</span>
                        {/if}
                        {#if academyCardProfiles[group.id].training_schedule}
                          <span>Πρόγραμμα: {academyCardProfiles[group.id].training_schedule}</span>
                        {/if}
                      </div>
                    {/if}
                    <div class="admin-audiences-count">
                      <strong>{group.active_players}</strong><span>ενεργοί παίκτες</span>
                    </div>
                    <button type="button" class="admin-secondary"
                      aria-expanded={focusedGroupId===group.id}
                      aria-controls="admin-group-workspace"
                      onclick={()=>focusedGroupId=focusedGroupId===group.id?'':group.id}
                      data-testid="admin-open-group">{focusedGroupId===group.id?'Κλείσιμο':'Διαχείριση τμήματος'} <span aria-hidden="true">›</span></button>
                  </li>
                {/each}
              </ul>
              {#if visibleGroups.length===0}
                <p class="admin-muted" role="status" data-testid="admin-group-search-empty">Δεν βρέθηκε τμήμα με αυτή την ονομασία. Δοκιμάστε διαφορετική αναζήτηση.</p>
              {/if}

            {/if}
          </section>
          {:else if focusedGroup}
                <section class="admin-group-workspace" id="admin-group-workspace" aria-labelledby="admin-focused-group-heading"
                  data-testid="admin-group-workspace">
                  <header class="admin-focused-group-header">
                    <div>
                      <p class="admin-eyebrow">ΤΜΗΜΑ · ΠΡΟΠΟΝΗΣΕΙΣ · ΑΘΛΗΤΕΣ</p>
                      <h3 id="admin-focused-group-heading">{focusedGroup.name}</h3>
                      <p class="admin-muted">{focusedGroup.active_players} ενεργοί παίκτες{groupPlacements[focusedGroup.id]?' · '+groupPlacements[focusedGroup.id].cityName+' / '+groupPlacements[focusedGroup.id].locationName:''}</p>
                    </div>
                    <button type="button" class="admin-secondary"
                      onclick={()=>focusedGroupId=''}>Πίσω στις ομάδες</button>
                  </header>
                  {#if organization && $auth.session}
                    {#key organization+':'+focusedGroup.id+':'+$auth.session.user.id}
                      <AdminGroupWorkspace group={focusedGroup} groups={directory.groups}
                        {organization} user={$auth.session.user}
                        canCreate={Boolean(canCreate)} canReadAttendance={canSeeAcademyAttendance}
                        onExpired={()=>auth.invalidateSession()}
                        onRevoked={()=>void auth.bootstrap()}
                        onChanged={()=>retry++}/>
                    {/key}
                  {/if}
                </section>
          {/if}
          {/if}
          <p class="admin-audiences-note">Οι λίστες παραληπτών ανακοινώσεων και ομαδικών συνομιλιών θα χρησιμοποιούν αυτές τις ίδιες αναθέσεις, με ανεξάρτητη επαλήθευση συμμετοχής πριν από την αποστολή. Η αποστολή μηνυμάτων δεν έχει ενεργοποιηθεί.</p>
        {/if}
      </div>
    {:else}
      <section class="admin-panel" data-testid="admin-audiences-denied">
        <h1>Δεν έχετε πρόσβαση σε αυτόν τον κατάλογο ομάδων.</h1>
        <a href="/admin">Επιστροφή στη διαχείριση</a>
      </section>
    {/if}
  </AdminFrame>
{:else}
  <AdminSessionStatus unavailable={$auth.status === 'unavailable'} busy={$auth.busy} error={$auth.error}
    onRetry={() => void auth.bootstrap()} onLogout={() => void auth.logout()}/>
{/if}

<style>
  .admin-audiences{width:100%;max-width:none;min-width:0}
  .admin-group-name-link{background:none;border:0;color:inherit;font:inherit;font-weight:800;text-align:left;padding:4px 0;cursor:pointer;min-height:44px;overflow-wrap:anywhere}
  .admin-group-name-link:hover,.admin-group-name-link:focus-visible{color:var(--admin-gold);text-decoration:underline}
  .admin-combined-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,280px),1fr));gap:16px}
  .admin-combined-owner{display:grid;align-content:start;gap:8px;min-width:0;padding:16px;border:1px solid var(--admin-line);border-radius:8px}
  .admin-combined-owner h3,.admin-combined-owner p,.admin-combined-owner ul{margin:0}
  .admin-combined-owner ul{list-style:none;padding:0;display:grid;gap:6px}
  .admin-combined-owner li{display:flex;flex-wrap:wrap;justify-content:space-between;gap:8px;padding:8px 0;border-bottom:1px solid var(--admin-line)}
  .admin-combined-owner a{color:var(--admin-gold);overflow-wrap:anywhere}
  .admin-combined-owner li span{color:inherit;font-size:12px}
  .admin-directory-switch{display:flex;gap:7px;flex-wrap:wrap;margin:14px 0}
  .admin-directory-switch button{min-height:40px;padding:9px 15px;border:1px solid #3c4140;
    background:#161818;color:#d5dad8;border-radius:7px;font:700 13px/1.3 'Fira Sans',sans-serif}
  .admin-directory-switch button[aria-pressed='true']{color:#fbd076;background:#241d10;border-color:#bd8528}

  .admin-audiences h1{font-size:clamp(23px,2.1vw,30px);line-height:1.2}
  .admin-audience-search{display:grid;gap:7px;max-width:440px;margin:17px 0 8px;
    font-size:12px;color:#bfc3c6}
  .admin-audience-search input{min-height:42px;border-radius:7px}
  .admin-audiences-list{display:grid!important;grid-template-columns:repeat(3,minmax(0,1fr));
    gap:13px;margin:14px 0 4px!important}
  .admin-audiences-list li{display:grid!important;grid-template-columns:1fr auto;gap:14px;
    align-content:space-between;min-height:174px;padding:17px!important;border:1px solid #303335!important;
    border-radius:11px;background:#141515;transition:border-color .15s,background .15s}
  .admin-audiences-list li:hover{border-color:#907139!important;background:#1a1915}
  .admin-group-hierarchy-filters{display:flex;gap:12px;flex-wrap:wrap;align-items:end;margin:12px 0 18px}
  .admin-group-hierarchy-filters label{display:grid;gap:5px;min-width:min(100%,215px);
    flex:1;font-size:12px;color:var(--admin-muted,#c5c7c7)}
  .admin-group-hierarchy-filters select{min-height:43px;width:100%}
  .admin-group-hierarchy-filters a{font-size:12px;color:var(--admin-accent,#f6bb38);
    min-height:43px;display:flex;align-items:center;text-decoration:underline}
  .admin-audiences-list li.admin-group-location-heading{grid-column:1/-1;display:flex!important;
    flex-wrap:wrap;align-items:center;min-height:0;border:0!important;
    border-bottom:1px solid var(--admin-accent,#f6bb38)!important;
    background:transparent!important;padding:12px 0 7px!important;border-radius:0;gap:10px}
  .admin-group-location-heading strong{font-size:17px;color:var(--admin-accent,#f6bb38)}
  .admin-group-location-heading span{font-size:13px;color:var(--admin-muted,#ccc)}
  .admin-group-placement{font-size:12px;color:var(--admin-muted,#c7c8c8)}

  .admin-audiences-list li.admin-audience-selected{border-color:#11af79!important;background:#101a16}
  .admin-audience-card-title{grid-column:1/-1;min-width:0;display:grid;gap:8px}
  .admin-audience-card-title strong{font:800 20px/1.2 'Fira Sans Extra Condensed',sans-serif;
    color:#f3f4f0}
  .admin-audience-card-meta{grid-column:1/-1;display:grid;gap:5px;color:#c0c8c9;
    font-size:11.5px;line-height:1.45;overflow-wrap:anywhere}
  .admin-audience-age{justify-self:start;padding:3px 8px;background:#10352a;
    color:#45dcaa;border-radius:5px;font-weight:750}
  .admin-audiences-list .admin-audiences-count{align-items:center;align-self:end}
  .admin-audiences-list .admin-audiences-count strong{font-size:21px}
  .admin-audiences-list .admin-secondary{min-height:38px;padding:7px 11px;font-size:12px;
    border-radius:7px;align-self:end}
  .admin-focused-group-header{display:flex;justify-content:space-between;align-items:center;gap:14px;flex-wrap:wrap}
  .admin-focused-group-header .admin-secondary{min-height:40px}
  @media(max-width:1020px){.admin-audiences-list{grid-template-columns:repeat(2,minmax(0,1fr))}}
  @media(max-width:700px){.admin-audiences-list{grid-template-columns:1fr}}

  .admin-audiences>h1{margin:0 0 10px}
  .admin-audiences>.admin-muted{max-width:80ch;margin-bottom:18px}
  .admin-audiences-select{display:flex;align-items:end;flex-wrap:wrap;gap:12px;margin-bottom:14px}
  .admin-audiences-select label{display:flex;flex-direction:column;gap:7px;font-size:12px;color:#b9bcbe}
  .admin-audiences-select select{width:min(100%,350px)}
  .admin-group-create{margin:0 0 15px}
  .admin-group-create-heading{display:flex;align-items:center;justify-content:space-between;flex-wrap:wrap;gap:12px}
  .admin-group-create-heading h2{margin:0}
  .admin-group-create-form{display:grid;gap:10px;max-width:500px;margin-top:16px}
  .admin-group-create-form>label{font-size:12px;color:#b9bcbe}
  .admin-group-create-form input,.admin-group-create-form textarea{width:100%}
  .admin-group-create-form label{display:grid;gap:5px}
  .admin-group-create-actions{display:flex;flex-wrap:wrap;gap:9px}
  .admin-academy-placement{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;
    padding:14px;border:1px solid #4b3b20;border-radius:10px;background:#161714}
  .admin-academy-placement label,.admin-inline-location label{display:grid;gap:6px;font-size:12px;color:#cfd0cc}
  .admin-academy-placement select,.admin-inline-location input{width:100%;min-width:0}
  .admin-add-city-link{align-self:end;min-height:41px;color:var(--admin-accent,#f6bb38);
    font-size:12px;display:inline-flex;align-items:center;text-decoration:underline}
  .admin-location-toggle{grid-column:1/-1;justify-self:start}
  .admin-location-note{grid-column:1/-1;margin:0}
  .admin-inline-location{grid-column:1/-1;display:grid;grid-template-columns:repeat(2,minmax(0,1fr));
    gap:10px;padding:15px;border:1px solid #62502a;border-radius:8px;background:#111313}
  .admin-inline-location h3,.admin-inline-location p,.admin-inline-location button{grid-column:1/-1;margin:0}
  .admin-inline-location button{justify-self:start}
  @media(max-width:620px){.admin-academy-placement,.admin-inline-location{grid-template-columns:1fr}}
  .admin-group-error{color:#ffd29b;margin-top:12px}
  .admin-audiences-stats{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:11px;margin:0 0 16px}
  .admin-scoped-empty{display:grid;gap:12px;justify-items:start;margin:12px 0 18px}
  .admin-scoped-empty h3,.admin-scoped-empty p{margin:0}
  .admin-scoped-empty a.admin-secondary{display:inline-flex;align-items:center;min-height:44px;text-decoration:none}
  .admin-audiences-stats>div{padding:16px;background:#121415;border:1px solid #303335;border-radius:12px;display:flex;flex-direction:column;gap:7px}
  .admin-audiences-stats span,.admin-audiences-caption,.admin-audiences-count span{font-size:12px;color:#aab0b3}
  .admin-audiences-stats strong{font-family:'Fira Sans Extra Condensed',sans-serif;font-size:29px;color:#ffca46}
  .admin-audiences-stats .admin-audiences-scope-text{font-family:inherit;font-size:14px;line-height:1.35;color:#f8e6ac;overflow-wrap:anywhere}
  .admin-audiences-heading{display:flex;justify-content:space-between;align-items:center;flex-wrap:wrap;gap:10px}
  .admin-audiences-heading h2{margin:0}
  .admin-audiences-list{margin:12px 0 0;padding:0;list-style:none}
  .admin-audiences-list li{border-top:1px solid #303335;padding:13px 0;display:flex;justify-content:space-between;align-items:center;gap:16px;min-height:66px}
  .admin-audiences-list li>div:first-child{display:grid;gap:5px;min-width:0}
  .admin-audiences-list li strong{overflow-wrap:anywhere}
  .admin-audiences-type{color:#ffca46;letter-spacing:.075em;font-size:10px}
  .admin-audiences-count{display:flex;align-items:baseline;gap:9px;white-space:nowrap}
  .admin-audiences-count strong{font-family:'Fira Sans Extra Condensed',sans-serif;color:#ffca46;font-size:23px}
  .admin-groups-organization-choice{min-width:min(100%,310px)!important;font-size:14px!important;color:#f6bb38!important}
  .admin-groups-organization-choice select{min-height:44px;padding:9px 12px;border:1px solid #8f783f;border-radius:7px;background:#181a1b;color:#fff}
  .admin-audience-actions{display:flex;flex-wrap:wrap;gap:9px;align-items:center}
  .admin-audience-actions a{display:inline-flex;align-items:center;justify-content:center;min-height:44px;padding:8px 12px;color:#ffca46;border:1px solid #70572a;border-radius:7px;font-size:14px}
  .admin-audience-actions a:hover{background:#f6bb3820;border-color:#ffca46}
  .admin-audience-actions a:focus-visible{outline:2px solid #ffca46;outline-offset:3px}
  .admin-group-workspace{margin-top:22px;padding:18px;border:1px solid #9e7834;background:linear-gradient(130deg,#242017,#101212);scroll-margin-top:100px}
  .admin-group-workspace header h3{font:italic 900 clamp(26px,2.4vw,39px)/1 'Fira Sans Extra Condensed',sans-serif;margin:0 0 9px;color:#fff}
  .admin-group-tools{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin:19px 0}
  .admin-group-tools>a{display:flex;align-items:center;min-height:48px;padding:12px 15px;
    color:#ffcf61;background:#181a1a;border:1px solid #8e723e;text-decoration:none;
    font-size:14px;font-weight:750}
  .admin-group-tools>a:hover{border-color:#ffdc80;background:#312614}
  .admin-group-tools>a:focus-visible{outline:3px solid #ffcc63;outline-offset:2px}
  @media(max-width:700px){.admin-group-tools{grid-template-columns:1fr}}
  .admin-group-create-next{padding:20px;border:1px solid #b78c38}
  .admin-group-create-next h2{font-size:25px;margin:4px 0 10px}
  .admin-group-create-next p{max-width:75ch;line-height:1.6}
  .admin-group-next-link{display:inline-flex;margin-top:12px;align-items:center;min-height:44px;
    padding:11px 16px;border:1px solid #b78c38;background:#241a0b;color:#ffd66e;
    font-weight:800;text-decoration:none}
  .admin-group-next-link:hover{background:#35220c}
  .admin-audiences-note{max-width:80ch;color:#b5babd;font-size:12px;margin-top:18px}
  @media(max-width:650px){.admin-audiences-stats{grid-template-columns:1fr}.admin-audiences-list li{align-items:start}.admin-audiences-count{flex-direction:column;align-items:flex-end;gap:2px}}
</style>
