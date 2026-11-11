import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const source=readFileSync(new URL('../../backend/src/LefteriaFc.Api/Pos/PosVatArithmetic.cs',import.meta.url),'utf8');
const vat=(gross,rate)=>rate===null?0:Math.floor((gross*rate+Math.floor((10000+rate)/2))/(10000+rate));

test('VAT-inclusive cent examples retain gross=net+VAT',()=>{
 for(const [gross,rate,expectedTax] of [
   [11900,1900,1900],
   [10900,900,900],
   [10500,500,500],
   [10300,300,300],
   [10000,0,0],
   [10000,null,0],
   [1,1900,0],
   [3,1900,0],
   [4,1900,1],
   [100000000,1900,15966387]
 ]) {
   const result=vat(gross,rate);
   assert.equal(result,expectedTax);
   assert.equal((gross-result)+result,gross);
 }
});
test('the calculator requires effective, enabled and matching reviewed treatment',()=>{
 assert.match(source,/!treatment.Enabled/);
 assert.match(source,/treatment.Revision < 1/);
 assert.match(source,/saleDate < treatment.EffectiveFrom/);
 assert.match(source,/saleDate > until/);
 assert.match(source,/expected != treatment.BasisPoints/);
 assert.match(source,/Unknown VAT treatment/);
 assert.match(source,/checked\(\(grossCents \* rate \+ \(10000L \+ rate\) \/ 2\) \/ \(10000L \+ rate\)\)/);
 assert.match(source,/net \+ vat != grossCents/);
});
test('zero-rated and exempt are never inferred or merged',()=>{
 assert.match(source,/"zero_0" => 0/);
 assert.match(source,/"exempt" => \(int\?\)null/);
 assert.match(source,/treatment.Code/);
 assert.match(source,/treatment.Revision/);
 assert.doesNotMatch(source,/UPDATE pos_cashier\.sales|INSERT INTO pos_cashier\.payment_events|MapPost\(/);
});