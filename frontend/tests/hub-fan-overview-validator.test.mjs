import test from 'node:test';
import assert from 'node:assert/strict';
import {validFanHubOverview} from '../src/lib/admin/hub-fan-overview.ts';

const owner='lefteria_fc_academy';
const example=()=>({
  organization_id:owner,
  as_of:'2026-11-15T18:00:00+02:00',
  counts:{
    total_records:5,pending:1,recorded_active:2,verified_active:1,
    past_due:1,cancelled:1,expired:0
  },
  plans:[
    {track:'parent',monthly_price_cents:1200,is_enabled:true,currency:'EUR',duration_days:30},
    {track:'both',monthly_price_cents:2200,is_enabled:false,currency:'EUR',duration_days:30}
  ]
});

test('fan overview accepts valid, isolated aggregate snapshot without identity data',()=>{
  assert.equal(validFanHubOverview(example(),owner),true);
});
test('fan overview never accepts cross-club, invalid time, or subscriber identity fields',()=>{
  assert.equal(validFanHubOverview(example(),'lefteria_fc'),false);
  assert.equal(validFanHubOverview({...example(),as_of:'invalid'},owner),false);
  assert.equal(validFanHubOverview({...example(),subscriber_id:'fake'},owner),false);
  assert.equal(validFanHubOverview({...example(),checkout_url:'https://sandbox.payabl.com/link'},owner),false);
});
test('verified counts cannot exceed recorded active, be negative or mismatch status totals',()=>{
  assert.equal(validFanHubOverview({...example(),counts:{...example().counts,verified_active:3}},owner),false);
  assert.equal(validFanHubOverview({...example(),counts:{...example().counts,active:-1}},owner),false);
  assert.equal(validFanHubOverview({...example(),counts:{...example().counts,pending:-1}},owner),false);
  assert.equal(validFanHubOverview({...example(),counts:{...example().counts,total_records:6}},owner),false);
  assert.equal(validFanHubOverview({...example(),counts:{...example().counts,verified_active:1.5}},owner),false);
});
test('pricing never accepts unbounded amounts, duplicate tracks or personal fields',()=>{
  const value=example();
  assert.equal(validFanHubOverview({...value,plans:[...value.plans,value.plans[0]]},owner),false);
  assert.equal(validFanHubOverview({...value,plans:[{...value.plans[0],monthly_price_cents:99}]},owner),false);
  assert.equal(validFanHubOverview({...value,plans:[{...value.plans[0],duration_days:365}]},owner),false);
  assert.equal(validFanHubOverview({...value,plans:[{...value.plans[0],subscriber_phone:'123'}]},owner),false);
  assert.equal(validFanHubOverview({...value,plans:[{...value.plans[0],track:'guardian'}]},owner),false);
});
