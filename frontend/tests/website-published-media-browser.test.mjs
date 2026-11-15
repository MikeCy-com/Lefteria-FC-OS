import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {compile} from 'svelte/compiler';
import {collectPublishedMedia,publishedMediaUrl} from '../src/lib/admin/published-media.ts';
import {adminSectionsFor} from '../src/lib/admin/module-catalog.ts';
const file=path=>readFileSync(new URL('../../'+path,import.meta.url),'utf8');
const owner='lefteria_fc',academy='lefteria_fc_academy';
const photo=(id,organization_id,image_url)=>({id,organization_id,title:id,image_url});
const base={
  organization_id:owner,
  gallery:[photo('one',owner,'/api/uploads/gallery/one.jpg'),photo('wrong',academy,'https://example.com/wrong.jpg'),
    photo('evil',owner,'javascript:alert(1)'),photo('bad',owner,'/admin/private.jpg')],
  sponsors:[{id:'gold',organization_id:owner,name:'Gold sponsor',logo_url:'https://example.com/gold.png',banner_url:null},
    {id:'academy',organization_id:academy,name:'Academy',logo_url:'https://example.com/academy.png'}],
  partners:[{id:'partner',organization_id:owner,name:'Partner',hero_image_url:'https://example.com/partner.png',headshot_url:null}],
  events:[{id:'match-day',organization_id:owner,title:'Day',cover_image_url:'https://example.com/gold.png'}]
};
test('only safe public URLs can become CMS image sources',()=>{
  for(const value of ['https://example.com/a.png','/images/a.jpg','/api/uploads/gallery/a.png'])
    assert.equal(publishedMediaUrl(value),value);
  for(const value of ['javascript:alert(1)','data:image/svg+xml,a','//example.com/x','http://example.com/a.jpg',
    'https://name:secret@example.com/photo.png','/app/photo','/admin/photo','/api/private/file','/api/admin/secret',
    'https://example.com/evil\\path','https://example.com/a\n',null,4])
    assert.equal(publishedMediaUrl(value),null,String(value));
});
test('public media inventory is strictly scoped, deduplicated, typed and does not invent records',()=>{
  const results=collectPublishedMedia(base,owner);
  assert.ok(results);
  assert.equal(results.length,3);
  assert.deepEqual(results.map(item=>item.source),['gallery','sponsor','partner']);
  assert.ok(results.every(item=>item.organization_id===owner));
  assert.ok(results.every(item=>!item.url.includes('academy')&&!item.url.includes('private')));
  assert.equal(results[1].alt,'Gold sponsor');
  assert.deepEqual(collectPublishedMedia({...base,gallery:[],sponsors:[],partners:[],events:[]},owner),[]);
  assert.equal(collectPublishedMedia({...base,organization_id:academy},owner),null);
  assert.equal(collectPublishedMedia({...base,events:null},owner),null);
  assert.equal(collectPublishedMedia(null,owner),null);
  assert.equal(collectPublishedMedia({...base,organization_id:academy,gallery:[photo('one',academy,'/academy.png')],
    sponsors:[],partners:[],events:[]},academy)?.[0].organization_id,academy);
});
test('media module, page and image picker require organization grants and leave upload unavailable',()=>{
  const user=(permissions,organizations)=>({id:'actor',role:'editor',name:'Actor',username:'actor',permissions,
    organizations:organizations.map(id=>({id,name:id}))});
  const ids=actor=>adminSectionsFor(actor).flatMap(section=>section.modules).map(module=>module.id);
  assert.ok(ids(user(['gallery:read'],[academy])).includes('media'));
  assert.ok(!ids(user(['news:read'],[academy])).includes('media'));
  assert.ok(!ids(user(['gallery:read'],[])).includes('media'));
  const page=file('frontend/src/routes/admin/media/+page.svelte');
  const frame=file('frontend/src/lib/admin/AdminFrame.svelte');
  const editor=file('frontend/src/routes/admin/website-pages/+page.svelte');
  const browser=file('frontend/src/lib/admin/PublishedMediaBrowser.svelte');
  assert.match(page,/hasAdminPermission\(\$auth\.session\.user\.permissions,'gallery:read'\)/);
  assert.match(page,/permitted\.some\(o=>o\.id===next\)/);
  assert.match(frame,/href="\/admin\/media"/);
  assert.match(browser,/credentials:'omit'/);
  assert.match(browser,/collectPublishedMedia\(data,owner\)/);
  assert.match(editor,/asset\.organization_id!==organization/);
  assert.match(editor,/draft\.blocks\[index\]\.url=url/);
  assert.match(editor,/onPick=\{usePublishedImage\}/);
  assert.doesNotMatch(browser,/\bPOST\b|\bDELETE\b|FormData/);
  for(const [path,source] of [
    ['src/routes/admin/media/+page.svelte',page],
    ['src/lib/admin/PublishedMediaBrowser.svelte',browser],
    ['src/routes/admin/website-pages/+page.svelte',editor]])
    assert.doesNotThrow(()=>compile(source,{filename:path,generate:'server'}),path);
});
