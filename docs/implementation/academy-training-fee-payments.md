# Academy training fees, Payabl checkout, parent preferences and Twilio SMS

Status: **code on draft PR #143 only, isolated Lefteria FC OS development branch**. The original Lefteria-FC repository, production website and production payment services remain untouched. No card data, PAN, CVV, expiry, salary/card payouts, card token, or banking credential is stored in the OS. Production payment collection and SMS sending remain disabled until authorized.

## Core billing rule: an issued month is a single charge

Training fees **do not create a twelve-month subscription**. Each fee is an explicit admin-issued `(academy, child, YYYY-MM)` record, with a unique database constraint and an integer amount in EUR cents. The finance administrator chooses only the months actually billed (for example, September–June can be issued one month at a time). The **preferred day** is a suggested due date for an *already issued* month. A value of 29, 30 or 31 clamps to the end of shorter calendar months. It does not generate another fee.

The Academy finance interface only allows issuance for a current, active, non-archived, non-transferred Academy player with a canonical, verified parent/guardian link. The linked parent identity is selected server-side; customer phone numbers are **never accepted from the admin's SMS payload**. One issued fee has exactly one assigned parent identity, one player, month, and amount. Additional guardians can be supported later through an explicit payer-assignment review; there is no automatic delegation of payment authority to unrelated accounts.

No payment attempt is allowed to modify the invoice amount. The admin issues an exact invoice first, and Payabl initiation is bound to it through `finance.attach_admin_training_fee_payment`. Request idempotency and invoice uniqueness prevent duplicate links from uncertain retries. The initial hosted checkout is separate from the invoice, so a link being ready is not a receipt. Once the restricted, verified Payabl callback moves the matching payment request to paid, the database trigger marks **only that issued child/month** paid, never other installments. Inconsistent order, amount, merchant/organization or guardian details fail closed or require reconciliation.

For refunds, chargebacks, disputed items or manual settlement discrepancies, **do not silently overwrite an issued fee**: create an independently reviewed adjustment/credit workflow and an immutable audit record first. That workflow is not yet implemented.

## Parent mobile app

The parent's **Οικονομικά** tab is an authenticated, role-specific feature. The read function rechecks the parent's current active identity, OTP-verified phone and canonical guardian link before returning any child finance data. It lists **only months explicitly issued for that parent/child**, an unpaid total, each child's name, amount and proposed due date, plus a Payabl card-checkout link only if a valid transaction is ready. A browser redirect does not change the invoice's paid status. The parent can refresh to see verified settlement.

The parent can choose the monthly preferred day (1–31), record `save_card_requested` and a distinct `automatic_collection_requested` opt-in. These are **preferences/consent requests only**. `automatic_collection_enabled` is constrained to false at the database level, and `saved_card_available` is false until merchant approval and a secure card-token lifecycle are available. No automatic monthly attempt currently runs. The UI must make that distinction clear. There is no API that accepts card numbers or CVV.

### Card saving and Click to Pay

Official Payabl Hosted Payment Page documentation states the optional Save Card feature is managed by Payabl and requires activation on the merchant account; it can store up to four cards for a returning shopper. The HPP may expose a checkbox for saving a card, and subsequent checkouts can use Payabl's `related_token_ids` after an approved integration. **The simplified callback signature does not authenticate this token field**. It must be verified with Payabl before binding it to a specific guardian; no OS token vault is implemented yet. Payabl's network Click to Pay also runs inside HPP, with production activation subject to merchant configuration.

Source:
- https://docs.payabl.com/docs/save-card
- https://docs.payabl.com/docs/click-to-pay
- https://docs.payabl.com/docs/credentials-on-file

Actual subsequent cardholder-initiated and off-session merchant-initiated transactions require contract approval, 3DS/consent, separate token storage and cryptographically verified transaction status. Approved recurring payments **must be scheduled only for existing issued, unpaid invoices** on or after the parent-selected day, with deterministic timezone `Asia/Nicosia`, idempotency and caps. Do not implement a yearly job that invents 12 installments. Never use the 'consent requested' boolean alone to authorize charging a saved card. Cancellation/withdrawal of consent must be available before activation.

## Finance backend and sending payment links

Admin → Finance → Payabl → Academy training fees:
1. Select the Academy organization and an active, verified child, enter an individual training month and exact amount (currently the existing child ID from the authenticated roster, with test payer email and names).
2. Create the invoice. When an approved Payabl sandbox merchant is configured, the adapter creates its unique hosted checkout link, attached to this exact fee before network initiation.
3. The link is visible in the backend for copy/open or for explicit Twilio SMS sending to the linked guardian. The parent can also view a ready link in-app.
4. The SMS action reserves an outbox attempt **before** network I/O and sends only to the currently verified guardian's phone (E.164). Twilio `201 Created` means the provider accepted the request, **not that the handset received it**. An uncertain response is recorded and does not automatically resend.
5. The admin monitors provider callbacks, pending settlement, and review exceptions. Payment success follows signed, correlated events, never a redirect or SMS status.

The Twilio client is disabled by default and uses `TwilioPaymentSms__AccountSid`, `TwilioPaymentSms__AuthToken`, `TwilioPaymentSms__MessagingServiceSid`, and `TwilioPaymentSms__Enabled=false`; keep credentials in isolated secrets, not the repo or UI. Payabl remains sandbox-only via `Payabl__Enabled=false`, `Payabl__Sandbox=true`, separate `PublicOrigin` and sandbox merchant credentials. The sender restricts destination to the verified guardian's number and the URL to approved Payabl sandbox HTTPS hosts. There is no bulk SMS sending, arbitrary recipient input or delivery success claim.

## Authorization and database controls

- Admin API has the existing session-cookie, `finance:read`/`finance:write`, scoped organization checks. Database definer functions independently revalidate manager/super_admin, current authorized session, organization grant and revocation locks before mutation.
- Mobile API has JWT authentication plus parent-only active identity, phone and current canonical guardian link checks at the database. Siblings under another guardian are not exposed.
- Runtime roles have no direct finance table privileges; explicit `ops/postgres/payabl-finance-grants.sql` grants only the appropriate callable functions.
- SMS event replay cannot initiate another send with the same client attempt ID; unknown outcomes need manual Twilio SID/recipient reconciliation.
- Financial events and payment statuses remain append-only or monotonic; no client may set `paid` and no generic finance permission authorizes employee payouts.

## Integration verification

Disposable PostgreSQL transactions:
- `ops/postgres/payabl-callback-state-checks.sql`: signed notices, pending vs credited APM states, exact cents, idempotent replay, wrong-order and wrong-amount rejection.
- `ops/postgres/payabl-training-fee-checks.sql`: one explicit child/month record, duplicate prevention, November capture applies only to November while December stays due, parent preferred day 31, no automatic MIT.

Static tests in `frontend/tests/admin-payabl-sandbox.test.mjs` assert no implicit annual billing, guardian ownership, callback-only settlement, no user-held raw card details and Twilio outbox guards. Offline .NET tests use a synthetic Twilio HTTP handler. The release workflow must build API, SvelteKit, Docker and regenerate/compare OpenAPI and TypeScript contract.

**Release blockers:** real Payabl merchant test credentials and approved card-on-file/CIT/MIT contract, HPP save-card and 3DS sandbox end-to-end verification, authenticated token/status enquiry, Twilio approved sender and account configuration, actual SMS delivery callback policy, dispute/refund handling, privacy notices and a full CI pass after code changes. Do not merge to main or enable production until those are verified.

## Invoice PDF parity to original Lefteria-FC (source audited)

**Read-only source:** `MikeCy-com/Lefteria-FC` `main` SHA
`91eafcc9efd01ad177b4f13a869cd5af7041e5b2`,
`backend/routes/charges.py::_build_receipt`, and
`frontend/src/pages/admin/ChargesTab.jsx`,
`frontend/src/mobile/pages/{ParentDashboard,FinancialPage}.jsx`.
Original sources have not been modified.

| Original behavior | Lefteria FC OS implementation |
|---|---|
| A4 PDF 210×297 mm | Existing SkiaSharp document backend, `AcademyInvoicePdf.cs`, A4 in physical mm |
| 42 mm black header, two gold diagonal accents and thin left gold strip | Geometric match at original locations, color black (18/255) and gold (#F5A623) |
| White circular Academy crest and Greek-capable text | Existing OS Academy crest, read-only `/app/club-logo.png` mount; DejaVu Greek font installed in API runtime |
| Greek club name, Academy, motto, right-aligned contact block | CMS-backed Academy/public club name, address, city/country, phone, email and website; no invented contact data or remote image request |
| Bold INVOICE title, gold rule and grey invoice-number/dates panel | Matching coordinates at x20/110 mm; immutable issue date, invoice number, **frozen** due date calculated once when month issued |
| Guardian and player details | Server-side verified payer name/email/phone, canonical child name/group; DOB intentionally blank until OS preserves trusted birth-date data |
| Black line-item table / alternating grey rows / gold total bar | Source matching section, correct billed amount in integer EUR cents, split into 3 rows/page when many lines instead of overflowing footer |
| Unpaid invoice | `Awaiting Payment`, original due date, outstanding balance; cannot be marked paid by viewing/download |
| Paid in cash | Manager-only explicit collection confirmation, idempotent receipt, `Cash`, receipt reference and `Paid on` from committed DB state |
| Paid by card | Signed matched Payabl sandbox capture (not return redirect), `Paid by Card`, provider reference and paid date |
| Same PDF available in parent app and admin CMS | JWT-authorized guardian + scoped admin finance endpoints, `Content-Disposition: attachment`, no-store and bounded `%PDF-` download verification |
| Original combined line-item / family wallet PDF | Up to 20 selected invoice IDs, exact child/parent scope, multiple PDF pages rather than footer overlap |

**Differences requiring review before claiming pixel perfection:** The renderer
uses OS DejaVu glyph metrics versus the original ReportLab’s available Noto /
DejaVu / Liberation fallback and the OS's actual crest asset; it does not have
a captured live rendered reference PDF to compare against. The original
ReportLab generator is copied at a layout/rule level, not byte-for-byte.
The original's multi-line amount calculation uses `collected_amount`, which
can display zero for unpaid invoices: OS displays the **correct issued charges**
rather than copying that billing error. The OS currently has only the
Academy month-specific invoice domain. The original also issued registration,
equipment, event, bank/online and partially paid charge documents, and had
guardian DOB/age fields. These are **not** automatically supported by the new
training-fee invoice until their own DB-backed, authorized charge models are
ported. Dynamic status is never derived from frontend fields.

**Accounting and legal gate:** Invoice/receipt numbering, statutory tax/VAT
fields, reversals, refunds, historical paid-document snapshots and permitted
payment-method changes need approval against Cyprus business requirements
before use as legal fiscal receipts. Unverified payments are never labelled
as settled. An existing Payabl payment link must be voided/reconciled before
a cash payment can be recorded; there is no dual-method double collection.

**Visual verification gate:** Produce the same sanitized 3 sample invoices
(unpaid, cash, card; plus 1 combined family PDF) from the original ReportLab
generator and the new SkiaSharp backend, render both to PNG at fixed DPI and
compare typography, crest scaling, baselines and spacing. No such dual-render
comparison has been completed because the branch CI cannot currently start
runners. Hence visual *intent* is source-matched but pixel-perfect status is
not yet certified.

