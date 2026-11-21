import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const file=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const home=file('lib/public/home-reference.css');
const shared=file('lib/public/club-polish.css');
const mobile=file('lib/mobile/components/mobile-theme.css');
const base=file('lib/public/public.css');

test('homepage display scale is smaller on desktop and phones',()=>{
  assert.ok(home.includes('font-size:clamp(40px,3.05vw,67px)'));
  assert.ok(home.includes('font-size:clamp(34px,7.7vw,40px)'));
  assert.ok(home.includes('font-size:clamp(21px,1.48vw,27px)'));
  assert.ok(home.includes('font-size:clamp(18px,1.18vw,23px)'));
  assert.ok(home.includes('text-wrap:balance;'));
  assert.ok(home.includes('white-space:normal;'));
});

test('page, card and promotional headings are compact without reducing reading text',()=>{
  for(const value of [
    '--public-title-size:clamp(35px,3.05vw,49px)',
    '--public-title-size:clamp(32px,8vw,39px)',
    'font-size:clamp(32px,2.8vw,45px)',
    'font-size:clamp(21px,1.7vw,25px)',
    'font-size:clamp(37px,3.9vw,55px)'
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
