import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { registerHooks } from 'node:module';
import { fileURLToPath } from 'node:url';
import { createServer } from 'node:http';
import { compile } from 'svelte/compiler';
import { render } from 'svelte/server';

function installHooks(origin) {
  return registerHooks({
    resolve(specifier, context, nextResolve) {
      if (specifier === '$app/state') return { url:'data:text/javascript,'+encodeURIComponent('export const page={url:new URL("https://fixture.test/events")};'),shortCircuit:true };
      if (specifier === '$app/env/private') return { url:'data:text/javascript,'+encodeURIComponent('export const INTERNAL_API_ORIGIN='+JSON.stringify(origin)),shortCircuit:true };
      if (specifier.startsWith('.') && /\/src\/(?:lib|routes)\//.test(context.parentURL ?? '') && !/\.[a-z]+$/i.test(specifier)) {
        const candidate = new URL(specifier+'.ts',context.parentURL);
        if (existsSync(candidate)) return nextResolve(candidate.href,context);
      }
      return nextResolve(specifier,context);
    },
    load(url, context, nextLoad) {
      if (url.endsWith('.css')) return {format:'module',source:'export {};',shortCircuit:true};
      if (url.endsWith('.svelte')) {
        const filename=fileURLToPath(url);
        return {format:'module',source:compile(readFileSync(filename,'utf8'),{filename,generate:'server'}).js.code,shortCircuit:true};
      }
      return nextLoad(url,context);
    }
  });
}

test('public catalogs preserve real filters, legacy selectors, shared records, safe source content and unavailable/unknown distinctions',async()=>{
  const club='lefteria_fc',academy='lefteria_fc_academy';
  const event={id:'Event--Case',organization_id:club,title:'Recorded community day',description:'## Recorded details\n\n<script>unsafe()</script> **Together**',event_type:'community',start_at:'2026-11-08T13:00:00Z',end_at:'2026-11-08T15:00:00Z',location:'Recorded ground',cover_image_url:null,capacity:0,price_eur:0,registration_url:'https://example.test/event',status:'live',is_featured:false};
  const past={...event,id:'past',title:'Recorded past event',start_at:'2026-10-08T13:00:00Z',end_at:null,event_type:'social'};
  const partner={id:'Partner-ID',organization_id:club,slug:'real-partner',name:'Recorded partner',partner_type:'technical',tagline:'Recorded partnership',hero_image_url:null,headshot_url:null,executive_name:'Public executive',executive_role:'Director',bio:'Recorded biography',bio_bullets:['Recorded achievement'],staff_members:[{name:'Public colleague',role:'Coach',photo_url:null,bio:'Recorded public biography'}],benefits:[{title:'Training',description:'Recorded benefit',icon:null}],website_url:'https://example.test/partner',socials:[{platform:'Unsafe',url:'javascript:alert(1)'}],cta_title:'Recorded contact',cta_text:'Ask our partner',cta_button1_label:'Contact',cta_button1_url:'https://example.test/contact',cta_button2_label:'Unsafe CTA',cta_button2_url:'javascript:alert(1)',display_order:1};
  const sponsor={id:'Sponsor/ID',organization_id:club,name:'Recorded sponsor',description:'Sponsor **description**',logo_url:null,banner_url:null,website:'https://example.test/sponsor',facebook:null,instagram:null,twitter:null,youtube:null,linkedin:null,level:'gold',sponsor_type:'first_team',display_order:1,content_blocks:[{id:'block',type:'text',title:'Recorded block',content:'<img src=x onerror=unsafe()>',image_url:null,link_url:'javascript:alert(1)',link_text:'Unsafe source link',images:[],items:[{label:'Established',value:'2024'}]}]};
  const stats={appearances:0,goals:0,assists:null,minutes_played:null,clean_sheets:null,yellow_cards:null,red_cards:null};
  const fixture={id:'match',organization_id:club,home_team:'Recorded club',away_team:'Recorded opponent',home_team_logo:null,away_team_logo:null,home_score:0,away_score:0,match_date:'2026-10-10',match_time:null,venue:null,competition:'Recorded competition',status:'Completed',team_id:null,academy_group_id:null,season:'2025/26',team_type:'First Team'};
  const archive={id:'Archive--ID',organization_id:club,season_id:'season',season_name:'2025/26',archived_at:'2026-10-31T10:00:00Z',team_type:'First Team',fixtures:[fixture],standings:[],player_stats:[{player_id:'player',name:'Public Player',number:9,image_url:null,statistics:stats}],top_scorer_name:null,top_scorer_goals:0};
  const empty={organization_id:club,players:[],staff:[],academy_groups:[],standings:[],venues:[],seasons:[],gallery:[],sponsors:[],partners:[],events:[],products:[],trial_settings:null,fixtures:[],tickets:[],archived_seasons:[],reviews:[],academy_stats:null,potm:{id:'month',organization_id:club,month_key:'2026-11',total_votes:0,results:[]},branding:null,modules:[],snapshot_at:'2026-10-31T00:00:00Z',generated_at:'2026-11-02T12:00:00Z',time_zone:'Asia/Nicosia'};
  const sections={
    [club]:{...empty,events:[event,past],partners:[partner],sponsors:[sponsor],archived_seasons:[archive]},
    [academy]:{...empty,organization_id:academy,events:[{...event,organization_id:academy}],partners:[{...partner,organization_id:academy}],sponsors:[{...sponsor,id:'academy-sponsor',organization_id:academy,sponsor_type:'academy'}],archived_seasons:[{...archive,organization_id:academy,team_type:'Academy',player_stats:[{...archive.player_stats[0],name:'Ανδρέας Πα.'}]}]}
  };
  let unavailable=false;
  const requests=[];
  const server=createServer((request,response)=>{
    const url=new URL(request.url,'http://fixture.test');const organization=url.searchParams.get('organization_id');
    requests.push({path:url.pathname,organization,cookie:request.headers.cookie,authorization:request.headers.authorization});
    response.setHeader('content-type','application/json');
    if(url.pathname==='/api/public/site'){response.end(JSON.stringify({organization_id:organization,club:null,teams:[],academy_groups:[],news:[],fixtures:[]}));return;}
    if(unavailable && organization===academy){response.statusCode=503;response.end('{}');return;}
    response.end(JSON.stringify(sections[organization]));
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  const installed=installHooks('http://127.0.0.1:'+server.address().port);
  try{
    const {loadPublicCatalog:list,loadPublicCatalogDetail:detail}=await import('../src/lib/server/public-catalog.ts');
    const url=path=>new URL(path,'https://preview.test');
    const events=await list(url('/events'),'events');
    assert.equal(events.cards.length,1);assert.equal(events.cards[0].title,event.title);assert.equal(events.cards[0].scopeLabel,'ΣΥΛΛΟΓΟΣ & ΑΚΑΔΗΜΙΑ');
    assert.equal((await list(url('/events?when=past&type=social'),'events')).cards[0].id,'past');
    assert.equal((await list(url('/events?when=all'),'events')).cards.length,2);
    assert.equal((await list(url('/events?scope=academy&when=all'),'events')).cards.length,1);
    await assert.rejects(list(url('/events?scope=other'),'events'),error=>error.status===400);
    await assert.rejects(list(url('/events?when=unknown'),'events'),error=>error.status===400);
    const partnerList=await list(url('/partners'),'partners');assert.equal(partnerList.cards.length,1);assert.match(partnerList.cards[0].href,/\/partners\/real-partner/);
    assert.equal((await detail(url('/partners/real-partner'),'partners','real-partner')).partner.id,partner.id);
    assert.equal((await detail(url('/partners/legacy'),'partners','recorded--Partner-ID')).partner.id,partner.id);
    assert.equal((await detail(url('/events/event'),'events',event.id)).event.id,event.id);
    await assert.rejects(detail(url('/events/event'),'events','event--case'),error=>error.status===404);
    const sponsors=await list(url('/sponsors/first-team?scope=academy'),'sponsors','first_team');assert.equal(sponsors.cards.length,1);assert.equal(sponsors.cards[0].organization,club);
    assert.match(sponsors.cards[0].href,/Sponsor%2FID/);
    const seasonList=await list(url('/seasons'),'seasons');assert.equal(seasonList.cards.length,2);
    const season=await detail(url('/seasons/Archive--ID'),'seasons','Archive--ID');assert.equal(season.archives.length,2);
    assert.equal((await detail(url('/seasons/Archive--ID?scope=academy'),'seasons','Archive--ID')).archives[0].organization_id,academy);
    await assert.rejects(detail(url('/seasons/missing'),'seasons','missing'),error=>error.status===404);
    const {default:List}=await import('../src/lib/public/PublicCatalogList.svelte');
    const {default:Detail}=await import('../src/lib/public/PublicCatalogDetail.svelte');
    const html=render(List,{props:{data:events}}).body;assert.match(html,/Recorded community day/);assert.match(html,/name="when"/);assert.match(html,/action="\/events"/);assert.doesNotMatch(html,/Recorded past event/);
    const eventHtml=render(Detail,{props:{data:await detail(url('/events/detail'),'events',event.id)}}).body;
    assert.match(eventHtml,/15:00/);assert.match(eventHtml,/Asia\/Nicosia/);assert.match(eventHtml,/Δωρεάν/);assert.match(eventHtml,/&lt;script>/);assert.doesNotMatch(eventHtml,/<script>unsafe/);
    const partnerHtml=render(Detail,{props:{data:await detail(url('/partners/real-partner'),'partners','real-partner')}}).body;
    assert.match(partnerHtml,/Public colleague/);assert.match(partnerHtml,/Recorded benefit/);assert.match(partnerHtml,/href="https:\/\/example.test\/contact"/);assert.doesNotMatch(partnerHtml,/href="javascript:|Unsafe CTA/);
    const sponsorHtml=render(Detail,{props:{data:await detail(url('/sponsors/detail?scope=first_team'),'sponsors',sponsor.id)}}).body;
    assert.match(sponsorHtml,/Recorded block/);assert.match(sponsorHtml,/&lt;img/);assert.doesNotMatch(sponsorHtml,/<img src=x|href="javascript:/);
    const archiveHtml=render(Detail,{props:{data:season}}).body;assert.match(archiveHtml,/0 : 0/);assert.match(archiveHtml,/Ανδρέας Πα\./);assert.match(archiveHtml,/>—<\/td>/);assert.doesNotMatch(archiveHtml,/null|Παπαδόπουλος|medical|parent_phone/);
    assert.ok(requests.every(row=>['/api/public/site','/api/public/sections'].includes(row.path)&&[club,academy].includes(row.organization)&&row.cookie===undefined&&row.authorization===undefined));
    sections[club].events=[];sections[academy].events=[];
    const noEvents=await list(url('/events'),'events');assert.equal(noEvents.cards.length,0);assert.match(render(List,{props:{data:noEvents}}).body,/ΔΕΝ ΥΠΑΡΧΟΥΝ ΚΑΤΑΧΩΡΙΣΕΙΣ/);
    unavailable=true;
    await assert.rejects(list(url('/events'),'events'),error=>error.status===503);
    await assert.rejects(detail(url('/partners/missing'),'partners','missing'),error=>error.status===503);
    assert.equal((await list(url('/partners?scope=first_team'),'partners')).cards.length,1);
  }finally{installed.deregister();await new Promise(resolve=>server.close(resolve));}
});
