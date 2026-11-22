import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {
  publicNavigation,navigationActive,navigationOwner,navigationLinkCurrent
} from '../src/lib/public/navigation.ts';
import {
  withWebsitePages,websitePagePath,websitePageParentLink,websiteSectionPath,embeddedWebsiteSections
} from '../src/lib/public/website-pages.ts';

const team='lefteria_fc',academy='lefteria_fc_academy';
const published=(organization_id,slug,menu_parent='root')=>({
  organization_id,slug,published_at:'2026-11-15T17:00:00Z',
  page:{title:'Δημοσιευμένη σελίδα '+slug,summary:'Σύντομη παρουσίαση',
    seo_title:'',seo_description:'',menu_parent,menu_order:0,
    blocks:[{type:'paragraph',text:'Δημοσιευμένο κείμενο',url:null}]}
});
const nav=(owner,rows)=>withWebsitePages(publicNavigation,rows,owner);
const active=(items,url,owner)=>items.filter(item=>navigationActive(item,url,owner)).map(item=>item.id);

test('same CMS slug in Academy and First Team has different current markers and a single owning section',()=>{
  const slug='football-guide',rows=[
    published(team,slug,'team'),published(academy,slug,'academy')
  ];
  const teamNav=nav(team,rows),academyNav=nav(academy,rows);
  const clubPath=websitePagePath(slug,team),academyPath=websitePagePath(slug,academy);
  assert.equal(clubPath,'/pages/football-guide');
  assert.equal(academyPath,'/pages/football-guide?organization_id=lefteria_fc_academy');
  assert.equal(navigationOwner(clubPath,team),null);
  assert.equal(navigationLinkCurrent(clubPath,clubPath),'page');
  assert.equal(navigationLinkCurrent(academyPath,academyPath),'page');
  assert.equal(navigationLinkCurrent(clubPath,academyPath),undefined);
  assert.equal(navigationLinkCurrent(academyPath,clubPath),undefined);
  assert.equal(navigationLinkCurrent(clubPath,clubPath+'?organization_id=invalid'),undefined);
  assert.deepEqual(active(teamNav,clubPath,team),['team']);
  assert.deepEqual(active(academyNav,academyPath,academy),['academy']);
  assert.deepEqual(active(teamNav,academyPath,team),[]);
  assert.deepEqual(active(academyNav,clubPath,academy),[]);
  assert.deepEqual(active(academyNav,new URL(academyPath,'https://example.test'),academy),['academy']);
  const teamItem=teamNav.find(item=>item.id==='team');
  const academyItem=academyNav.find(item=>item.id==='academy');
  const teamMenu=teamItem.items.find(row=>row[0].startsWith('page-'))[2];
  const academyMenu=academyItem.items.find(row=>row[0].startsWith('page-'))[2];
  assert.equal(teamMenu,'/team#website-section-football-guide');
  assert.equal(academyMenu,'/academy#website-section-football-guide');
  assert.equal(navigationLinkCurrent(teamMenu,teamMenu),'location');
  assert.equal(navigationLinkCurrent(academyMenu,academyMenu),'location');
  // Standalone legacy bookmarks retain owning menu category, without
  // claiming that their anchor is the currently loaded URL.
  assert.equal(navigationLinkCurrent(teamMenu,clubPath),undefined);
  assert.equal(navigationLinkCurrent(academyMenu,academyPath),undefined);
});

test('Pages menu links to native sections with scoped content and keeps old URLs accessible',()=>{
  const rows=[
    published(team,'first-roster','team'),
    published(team,'match-guide','matches'),
    published(academy,'kids-schedule','academy'),
    published(academy,'academy-about','club'),
    published(team,'club-info','root')
  ];
  assert.equal(websiteSectionPath('first-roster',team,'team'),'/team#website-section-first-roster');
  assert.equal(websiteSectionPath('match-guide',team,'matches'),'/team?tab=results#website-section-match-guide');
  assert.equal(websiteSectionPath('kids-schedule',academy,'academy'),'/academy#website-section-kids-schedule');
  assert.equal(websiteSectionPath('academy-about',academy,'club'),'/about?organization_id=lefteria_fc_academy#website-section-academy-about');
  assert.equal(websiteSectionPath('club-info',team,'root'),'/#website-section-club-info');
  assert.equal(websiteSectionPath('academy-info',academy,'root'),'/academy#website-section-academy-info');
  assert.equal(websiteSectionPath('academy-shop',academy,'shop'),websitePagePath('academy-shop',academy));
  assert.deepEqual(
    embeddedWebsiteSections(rows,team,'/team',new URLSearchParams()).map(x=>x.slug),['first-roster']
  );
  assert.deepEqual(
    embeddedWebsiteSections(rows,team,'/team',new URLSearchParams('tab=results')).map(x=>x.slug),
    ['first-roster','match-guide']
  );
  assert.deepEqual(
    embeddedWebsiteSections(rows,academy,'/academy',new URLSearchParams()).map(x=>x.slug),['kids-schedule']
  );
  assert.deepEqual(
    embeddedWebsiteSections(rows,academy,'/about',new URLSearchParams()).map(x=>x.slug),[]
  );
  assert.deepEqual(
    embeddedWebsiteSections(rows,academy,'/about',new URLSearchParams('organization_id=lefteria_fc_academy')).map(x=>x.slug),
    ['academy-about']
  );
  assert.equal(websitePagePath('kids-schedule',academy),'/pages/kids-schedule?organization_id=lefteria_fc_academy');
  const shell=readFileSync(new URL('../src/lib/public/PublicShell.svelte',import.meta.url),'utf8');
  assert.match(shell,/embeddedWebsiteSections/);
  assert.match(shell,/id=\{embeddedWebsiteSectionId\(section.slug\)\}/);
  assert.match(shell,/WebsitePageBlocks blocks=\{section.page.blocks\}/);
  assert.ok(compile(shell,{filename:'PublicShell.svelte',generate:'server'}).js.code.length>100);
});

test('root, news and contact CMS pages correctly illuminate their own rail or mobile category',()=>{
  const rows=[published(team,'legal-article','root'),published(team,'latest-news-insight','news'),
    published(team,'contact-more','contact')];
  const items=nav(team,rows);
  assert.deepEqual(active(items,websitePagePath('legal-article',team),team),
    ['page-lefteria_fc-legal-article']);
  assert.deepEqual(active(items,websitePagePath('latest-news-insight',team),team),['news']);
  assert.deepEqual(active(items,websitePagePath('contact-more',team),team),['contact']);
  assert.deepEqual(active(items,'/news',team),['news']);
  assert.deepEqual(active(items,'/contact',team),['contact']);
  assert.equal(navigationLinkCurrent('/pages/legal-article','/pages/legal-article#detail'),'page');
  assert.equal(navigationLinkCurrent('/pages/legal-article#detail','/pages/legal-article'),undefined);
});

test('published CMS return destinations lead back to the correct organization or menu section',()=>{
  assert.deepEqual(websitePageParentLink(academy,'root'),{href:'/academy',label:'ΑΚΑΔΗΜΙΑ'});
  assert.deepEqual(websitePageParentLink(academy,'hidden'),{href:'/academy',label:'ΑΚΑΔΗΜΙΑ'});
  assert.deepEqual(websitePageParentLink(team,'root'),{href:'/team',label:'ΠΡΩΤΗ ΟΜΑΔΑ'});
  assert.deepEqual(websitePageParentLink(team,'hidden'),{href:'/team',label:'ΠΡΩΤΗ ΟΜΑΔΑ'});
  assert.deepEqual(websitePageParentLink(academy,'news'),{
    href:'/news?organization_id=lefteria_fc_academy',label:'ΝΕΑ ΑΚΑΔΗΜΙΑΣ'
  });
  assert.deepEqual(websitePageParentLink(team,'news'),{
    href:'/news?organization_id=lefteria_fc',label:'ΝΕΑ ΠΡΩΤΗΣ ΟΜΑΔΑΣ'
  });
  assert.deepEqual(websitePageParentLink(academy,'academy'),{href:'/academy',label:'ΑΚΑΔΗΜΙΑ'});
  assert.deepEqual(websitePageParentLink(team,'matches'),{href:'/team?tab=results',label:'ΑΓΩΝΕΣ'});
  assert.deepEqual(websitePageParentLink(team,'club'),{href:'/about',label:'Ο ΣΥΛΛΟΓΟΣ'});
  assert.deepEqual(websitePageParentLink(academy,'club'),{
    href:'/about?organization_id=lefteria_fc_academy',label:'Ο ΣΥΛΛΟΓΟΣ'
  });
  assert.deepEqual(websitePageParentLink(academy,'events'),{
    href:'/events?scope=academy',label:'ΕΚΔΗΛΩΣΕΙΣ'
  });
  assert.deepEqual(websitePageParentLink(academy,'contact'),{
    href:'/contact?organization_id=lefteria_fc_academy',label:'ΕΠΙΚΟΙΝΩΝΙΑ'
  });
  assert.deepEqual(websitePageParentLink(academy,'shop'),{
    href:'/shop',label:'ΚΑΤΑΣΤΗΜΑ'
  });
  assert.deepEqual(websitePageParentLink(team,'contact'),{href:'/contact',label:'ΕΠΙΚΟΙΝΩΝΙΑ'});
  assert.deepEqual(websitePageParentLink(team,'events'),{
    href:'/events?scope=first_team',label:'ΕΚΔΗΛΩΣΕΙΣ'
  });
});

test('published page retains semantic article, escaped editor blocks, SEO and polished navigable masthead',()=>{
  const source=readFileSync(new URL('../src/routes/pages/[slug]/+page.svelte',import.meta.url),'utf8');
  const navigation=readFileSync(new URL('../src/lib/public/PublicNavigation.svelte',import.meta.url),'utf8');
  assert.ok(compile(source,{filename:'pages/[slug]/+page.svelte',generate:'server'}).js.code.length>100);
  assert.match(source,/data-testid="website-published-page"/);
  assert.match(source,/data-testid="website-page-breadcrumbs"/);
  assert.match(source,/aria-label="Διαδρομή πλοήγησης"/);
  assert.match(source,/aria-current="page"/);
  assert.match(source,/href=\{parentLink.href\}/);
  assert.match(source,/data-testid="website-page-return"/);
  assert.match(source,/websitePageParentLink\(data.organization, content.menu_parent\)/);
  assert.match(source,/<WebsitePageBlocks blocks=\{content.blocks\}/);
  assert.match(source,/publicCanonicalHref\(canonicalPath\)/);
  assert.match(source,/seo_description \|\| content.summary/);
  assert.match(navigation,/class:is-current=\{navigationActive\(item, page.url, organization\)\}/);
  assert.match(source,/@media\(max-width:650px\)/);
  assert.match(source,/outline:3px solid #f6bb38/);
  assert.doesNotMatch(source,/<main\b|\{@html\}|onclick=\{.*publish/);
});
