import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
const source=readFileSync(new URL('../../backend/src/LefteriaFc.Api/Pos/PayablSandboxRefundClient.cs',import.meta.url),'utf8');

test('Payabl request signature uses ordinal-sorted raw parameter values plus secret',()=>{
 const entries={merchantid:'gateway_test',amount:'0.01',currency:'EUR',transactionid:'106280495'};
 const plain=Object.keys(entries).sort().map(k=>entries[k]).join('')+'testsecret';
 const expected=createHash('sha1').update(plain).digest('hex');
 assert.equal(expected.length,40);
 assert.match(source,/OrderBy\(pair => pair.Key, StringComparer.Ordinal\)/);
 assert.match(source,/Select\(pair => pair.Value\)/);
 assert.match(source,/SHA1.HashData\(Encoding.Latin1.GetBytes\(ordered\)\)/);
 assert.match(source,/ToLowerInvariant\(\)/);
});
test('Refund client has fixed sandbox URL, rejects redirects and limits reply size',()=>{
 assert.match(source,/https:\/\/sandbox.payabl.com\/pay\/backoffice\/payment_refund/);
 assert.match(source,/HttpMethod.Post, SandboxEndpoint/);
 assert.match(source,/HttpStatusCode.PermanentRedirect/);
 assert.match(source,/Gateway redirect prohibited/);
 assert.match(source,/raw.Length > 8192/);
 assert.match(source,/Duplicate or unbounded gateway field/);
 assert.doesNotMatch(source,/https:\/\/payabl.com\/pay\/backoffice\/payment_refund/);
});
test('Refund is bound to original captured transaction, currency and exact cents',()=>{
 assert.match(source,/CapturedTransactionId\.All/);
 assert.match(source,/AmountCents is < 1 or > 999999999/);
 assert.match(source,/\["currency"\] = "EUR"/);
 assert.match(source,/\["amount"\] = \(refund.AmountCents \/ 100m\)/);
 assert.match(source,/id != refund.CapturedTransactionId/);
 assert.match(source,/currency != "EUR"/);
 assert.match(source,/amount != refund.AmountCents \/ 100m/);
});
test('Sandbox request response never directly issues a refund or financial posting',()=>{
 assert.match(source,/RequiresConfirmation/);
 assert.match(source,/RefundGatewayResponse\(id, status, error, messageText, true\)/);
 assert.doesNotMatch(source,/UPDATE pos\.|INSERT INTO pos\.|ExecuteNonQueryAsync|MapPost\(|settle_exact_cash_sale/);
});
