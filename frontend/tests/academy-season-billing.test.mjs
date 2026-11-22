import test from 'node:test';
import assert from 'node:assert/strict';
import {academyFamilyRate,academySeasonQuote} from '../src/lib/academy/season-billing.ts';

test('family tariff is 60/100/150 and grows 50 per extra child',()=>{
  assert.deepEqual([1,2,3,4,5].map(academyFamilyRate),[60,100,150,200,250]);
  assert.throws(()=>academyFamilyRate(0));
});
test('September through June is ten months, never July or August',()=>{
  const q=academySeasonQuote(3,'2026-09-01',2026);
  assert.equal(q.months.length,10);
  assert.equal(q.months[0].month,'2026-09');
  assert.equal(q.months.at(-1).month,'2027-06');
  assert.equal(q.remainingTuitionEur,1500);
  assert.equal(q.registrationEur,60);
  assert.equal(q.kitEur,420);
  assert.equal(q.printingEur,30);
  assert.equal(q.initialEstimatedEur,660);
});
test('December 16 has half-price December then full January–June',()=>{
  const q=academySeasonQuote(3,'2026-12-16',2026);
  assert.deepEqual(q.months.map(m=>m.month),
    ['2026-12','2027-01','2027-02','2027-03','2027-04','2027-05','2027-06']);
  assert.equal(q.months[0].amountEur,75);
  assert.equal(q.months[0].prorated,true);
  assert.equal(q.remainingTuitionEur,975);
  assert.equal(q.initialEstimatedEur,585);
});
test('the 15th is full; single child after the 15th is 30 euros',()=>{
  assert.equal(academySeasonQuote(1,'2026-12-15',2026).months[0].amountEur,60);
  assert.equal(academySeasonQuote(1,'2026-12-16',2026).months[0].amountEur,30);
});
test('July/August enrollment starts at September, no off-season charges',()=>{
  assert.equal(academySeasonQuote(2,'2026-07-20',2026).months[0].month,'2026-09');
  assert.equal(academySeasonQuote(2,'2026-08-16',2026).months[0].amountEur,100);
});
test('next season has shifted dates and June late enrollment still partial',()=>{
  assert.equal(academySeasonQuote(1,'2027-09-20',2027).months[0].month,'2027-09');
  const q=academySeasonQuote(1,'2027-06-16',2026);
  assert.equal(q.months.length,1);
  assert.equal(q.months[0].amountEur,30);
  assert.throws(()=>academySeasonQuote(1,'2027-07-01',2026));
});
test('reject malformed dates including 2027-02-30',()=>{
  assert.throws(()=>academySeasonQuote(1,'2027-02-30',2026));
  assert.throws(()=>academySeasonQuote(1,'2026-13-01',2026));
  assert.throws(()=>academySeasonQuote(1,'2026-12-16Z',2026));
});
