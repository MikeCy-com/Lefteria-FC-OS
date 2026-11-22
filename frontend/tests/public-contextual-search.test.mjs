import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {publicSearch, publicNavigation, academyGroupLink} from '../src/lib/public/navigation.ts';
import {withWebsitePages, websiteSectionPath} from '../src/lib/public/website-pages.ts';

const page = (organization_id, slug, title, menu_parent='root') => ({
  organization_id,slug,published_at:'2026-11-15T12:00:00Z',
  page:{title,summary:'',seo_title:'',seo_description:'',
    menu_parent,menu_order:0,blocks:[{type:'paragraph',text:'Published page',url:null}]}
});
const academy='lefteria_fc_academy',team='lefteria_fc';
const academyCms=page(academy,'academy-training-guide','Οδηγός Ακαδημίας');
const teamCms=page(team,'first-team-history','Ιστορία Πρώτης Ομάδας');

test('contextual search shows only its organization hubs and scope-specific sponsor routes',()=>{
  const academyResults=publicSearch('',[],[],{organization:academy});
  const firstResults=publicSearch('',[],[],{organization:team});
  assert.ok(academyResults.length>0);
  assert.ok(firstResults.length>0);
  assert.ok(academyResults.every(row=>!row.path.startsWith('/team') &&
    !row.path.startsWith('/trials') && !row.path.startsWith('/vote')));
  assert.ok(firstResults.every(row=>!row.path.startsWith('/academy')));
  assert.deepEqual(publicSearch('ΑΠΟΤΕΛΕΣΜΑΤΑ',[],[],{organization:academy}),[]);
  assert.ok(publicSearch('ΑΠΟΤΕΛΕΣΜΑΤΑ',[],[],{organization:team})
    .some(row=>row.path==='/team?tab=results'));
  assert.equal(publicSearch('Χορηγοί ακαδημίας',[],[],{organization:team}).length,0);
  assert.ok(publicSearch('Χορηγοί ακαδημίας',[],[],{organization:academy})
    .some(row=>row.path==='/sponsors/academy'));
  assert.equal(publicSearch('Συνεργάτες ομάδας',[],[],{organization:academy}).length,0);
  assert.equal(publicSearch('Συνεργάτες ακαδημίας',[],[],{organization:team}).length,0);
  assert.ok(publicSearch('Συνεργάτες',[],[],{organization:academy})
    .some(row=>row.path==='/partners?scope=academy'));
  assert.ok(publicSearch('Συνεργάτες',[],[],{organization:team})
    .some(row=>row.path==='/partners?scope=first_team'));
  assert.ok(publicSearch('Προϊόντα συλλόγου',[],[],{organization:team})
    .some(row=>row.path==='/shop'));
  assert.equal(publicSearch('Εισιτήρια',[],[],{organization:team}).length,0,
    'The shop cannot claim to be a ticket-booking route');
});

test('search includes only published CMS pages from the current organization',()=>{
  const academyNav=withWebsitePages(publicNavigation,[teamCms,academyCms],academy);
  const firstNav=withWebsitePages(publicNavigation,[academyCms,teamCms],team);
  const a=publicSearch('Οδηγός Ακαδημίας',[],[],{organization:academy,navigation:academyNav});
  assert.deepEqual(a.map(item=>item.path),[websiteSectionPath(academyCms.slug,academy,academyCms.page.menu_parent)]);
  assert.deepEqual(publicSearch('Οδηγός Ακαδημίας',[],[],{organization:team,navigation:firstNav}),[]);
  assert.deepEqual(publicSearch('Ιστορία Πρώτης Ομάδας',[],[],{
    organization:team,navigation:firstNav
  }).map(item=>item.path),[websiteSectionPath(teamCms.slug,team,teamCms.page.menu_parent)]);
  assert.deepEqual(publicSearch('Ιστορία Πρώτης Ομάδας',[],[],{
    organization:academy,navigation:academyNav
  }),[]);
  const manuallyCrossWired=withWebsitePages(publicNavigation,[teamCms],team);
  assert.deepEqual(publicSearch('Ιστορία Πρώτης Ομάδας',[],[],{
    organization:academy,navigation:manuallyCrossWired
  }),[],'Client filtering must reject a mistakenly injected CMS page');
});

test('groups and published news stay in the correct organization, even with mixed public data',()=>{
  const groups=[
    {id:'academy-u12',organization_id:academy,name:'Δοκιμή U12'},
    {id:'foreign',organization_id:team,name:'Δοκιμή First Team'}
  ];
  const news=[
    {id:'academy-news',organization_id:academy,title:'Δοκιμή Νέα Ακαδημίας'},
    {id:'team-news',organization_id:team,title:'Δοκιμή Νέα Πρώτης Ομάδας'}
  ];
  const a=publicSearch('δοκιμη',groups,news,{organization:academy});
  assert.equal(a.length,2);
  assert.ok(a.some(x=>x.path===academyGroupLink(groups[0])));
  assert.ok(a.some(x=>x.path.includes('academy-news') &&
    x.path.includes('organization_id=lefteria_fc_academy')));
  assert.ok(a.every(x=>!x.path.includes('foreign') && !x.path.includes('team-news')));
  const t=publicSearch('δοκιμη',groups,news,{organization:team});
  assert.equal(t.length,1);
  assert.match(t[0].path,/team-news/);
  assert.match(t[0].path,/organization_id=lefteria_fc/);
  assert.ok(t.every(x=>!x.path.includes('academy-u12') && !x.path.includes('academy-news')));
  assert.ok(publicSearch('ΝΕΑ',[],[],{organization:academy})
    .some(x=>x.path==='/news?organization_id=lefteria_fc_academy'));
  assert.ok(publicSearch('ΝΕΑ',[],[],{organization:team})
    .some(x=>x.path==='/news?organization_id=lefteria_fc'));
});

test('search UI compiles, scopes backend news and announces keyboard-accessible results',()=>{
  const root=new URL('../src/lib/public/',import.meta.url);
  const search=readFileSync(new URL('SiteSearch.svelte',root),'utf8');
  const nav=readFileSync(new URL('PublicNavigation.svelte',root),'utf8');
  const css=readFileSync(new URL('club-polish.css',root),'utf8');
  assert.ok(compile(search,{filename:'SiteSearch.svelte',generate:'server'}).js.code.length>100);
  assert.match(nav,/<SiteSearch \{groups\} \{organization\} navigation=\{websiteNavigation\} \/>/);
  assert.match(search,/publicSearch\(query, groups, news, \{ organization, navigation \}\)/);
  assert.match(search,/organization_id: sourceOrganization/);
  assert.match(search,/article\.organization_id === sourceOrganization/);
  assert.match(search,/AbortSignal\.any\(\[controller\.signal, AbortSignal\.timeout\(10000\)\]\)/);
  assert.match(search,/data-testid="public-search-count"/);
  // Navigation and current search matches remain countable while the news
  // request is pending. The loading message must not replace the live count.
  assert.match(search,/\{results\.length\} \{results\.length === 1/);
  assert.match(search,/\{loading \? ' · Ενημερώνονται τα δημοσιευμένα νέα…' : ''\}/);
  assert.doesNotMatch(search,/loading \? 'Αναζήτηση δημοσιευμένων νέων…'/);
  assert.match(search,/data-testid="public-search-loading"/);
  assert.match(search,/aria-live="polite"/);
  assert.match(search,/aria-controls="public-search-results"/);
  assert.match(search,/aria-label="Αποτελέσματα αναζήτησης"/);
  assert.match(search,/data-testid="public-search-context"/);
  assert.match(css,/\.public-search-results a:focus-visible/);
  assert.match(css,/@media\(max-width:600px\)/);
});
