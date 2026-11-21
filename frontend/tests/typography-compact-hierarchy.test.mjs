import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const file=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const home=file('lib/public/home-reference.css');
const shared=file('lib/public/club-polish.css');
const mobile=file('lib/mobile/components/mobile-theme.css');
const base=file('lib/public/public.css');

test('homepage display scale is smaller on desktop and phones',()=>{
  assert.ok(home.includes("font:italic 900 clamp(48px,3.5vw,58px)/1 'Fira Sans Extra Condensed'"));
  assert.ok(home.includes('font-size:clamp(34px,8.6vw,40px)'));
  assert.ok(home.includes('font:800 clamp(19px,1.25vw,22px)'));
  assert.ok(home.includes('font:800 17px/1.2'));
  assert.ok(home.includes('text-wrap:balance;'));
  assert.ok(home.includes('white-space:normal;'));
});

test('page, card and promotional headings are compact without reducing reading text',()=>{
  for(const value of [
    '--public-title-size:58px',
    '--public-title-size:38px',
    'font-size:clamp(38px,3.5vw,58px)',
    "font:800 clamp(20px,1.5vw,24px)/1.2 'Fira Sans Extra Condensed'", 
    'font-size:clamp(39px,3.5vw,58px)'
  ]) assert.ok(shared.includes(value),'Missing '+value);
  assert.ok(base.includes('font-size:16px;line-height:1.6'));
});

test('member app headings are smaller while labels, body and form sizes stay readable',()=>{
  for(const value of [
    'font-size:clamp(26px,3.3vw,34px)',
    'font-size:clamp(26px,6.8vw,29px)',
    'font-size:clamp(18px,1.9vw,21px)',
    '--lf-type-body: 13px',
    '--lf-type-label: 12px',
    'font-size:max(16px,1rem)',
    'min-height: 44px'
  ]) assert.ok(mobile.includes(value),'Missing '+value);
});
