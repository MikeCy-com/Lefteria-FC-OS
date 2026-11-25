# Academy player finance · safe future-fee cancellation

**Status:** Development branch for review and disposable CI only. Not deployed to production, not a refund or payment-provider cancellation. This slice intentionally does not grant accountant, city-manager or location-manager access to the global invoice ledger.

## Existing contract and reason for change

The canonical Academy monthly charge is a real issued `finance.training_fee_issues` invoice. Issuance is per one child and one explicitly selected month (not an automatic annual subscription). Manual cash settlement and signed Payabl sandbox captures create separate immutable evidence. Prior accounting aggregates summed every issued fee and lacked a cancel state, so deleting invoices would remove audit history and distort statistics. Hard-deleting paid invoices is prohibited.

## Implemented workflow

- Super Admin opens **Admin → Groups → Players CRM → player → Charges & documents**, which deep-links to the existing player-filtered finance screen. Site managers may not use the global finance endpoint. Existing manual cash receipt and sandbox checkout workflows remain unchanged.
- On an **unpaid future month** with **no attached provider payment request or SMS attempt**, an authorized Super Admin selects **Cancel future charge**, enters an 8–240 character reason, and confirms the child/month.
- A single auditable, PostgreSQL-locked transaction changes the invoice to `cancelled`, retains its ID, amount, date and frozen payer, records actor, time, reason and unique idempotency request, and appends an independent security audit event. A repeat of the identical request only returns a replay response; a conflicting request fails closed.
- Cancelling this invoice does **not** delete or refund any money, disable or alter a Payabl transaction, delete guardian records, change player team assignment or issue additional months.
- Month boundaries use **Asia/Nicosia**. A current or older month, already-paid receipt, linked Payabl request, SMS in flight or reconciliation case requires separate human review rather than silent cancellation.
- Super Admin finance history displays the cancelled invoice and reason. It is no longer offered as payable or downloadable as an unpaid invoice; current parent wallet and parent PDF exclude cancellation status.
- Accounting and site reporting keep paid amounts unchanged, exclude cancelled charges from active issued/receivable totals, and show a separate **cancelled count and amount** for reconciliation. Location attribution stays immutable.
- The original `(organization_id,player_id,fee_month)` uniqueness remains in place: cancelling cannot be used to secretly reissue or charge the same month. A reviewed corrective credit/reissue workflow would be a separate release.

## Access and verification

- The HTTP endpoint requires authenticated Super Admin with `finance:write` and Academy organization grant, with a second independently verified SQL session/org/role check.
- PostgreSQL runtime roles have **no direct table DELETE/UPDATE**; the admin role gets EXECUTE for the single limited function, parent app role is explicitly forbidden.
- New EF migration extends the charge state; previously defined parent/admin/pdf/accounting/site reader functions are replaced with compatible status filters and audit projections.
- Disposable rollback-only SQL checks validate role denial, idempotent retry, one audit event, cancelled-vs-paid statistics, history preservation, PDF denial, paid cash receipt safety, and linked-provider denial.
- CI has SQL gate, Svelte compile/static contract tests and existing .NET, SSR, API contract, browser and Docker gates. Independent staging verification and any live deployment are still required.
- **Rollback:** `Down()` refuses to erase the new status if any cancellation has been recorded; recover forward with an authorized finance migration rather than erase audit records.

## Explicitly not delivered in this slice

- Editing a child's personal details, bio or profile picture: requires child-specific privacy and photo-consent enforcement plus secure per-location authorization.
- Batch cancellation of future months when a player leaves: invoices are currently issued one month at a time. Each existing future invoice may be individually cancelled; a batch command with independent checks and exact totals can follow after verified staging.
- Accountant approval / manual transfer or bank settlement and broader manager roles: accountant currently has read-only aggregated finance visibility; assigning global invoice write privilege would violate the existing role boundary.
- Refunds, credits, provider reversals, chargebacks, paid invoice cancellation, and duplicate-posted financial reconciliation are not automated.
- Live Hostinger and the original `Lefteria-FC` app are untouched.
