# Academy registration: original five-step workflow and fail-closed intake

## Source-of-truth design

The original Lefteria-FC `frontend/src/pages/RegistrationPage.jsx` is the reference for the user-visible form and sequence:

1. Player identity and address
2. Guardian and emergency contact
3. Allergies, diagnoses/conditions and medication
4. Participation, emergency medical permission, privacy notice, explicit optional media consent (yes **or no**), optional communications, liability/financial acknowledgements
5. Preferred payment method, parent signature and date

The OS page remains `/academy/registration`. It must be visually tested against the original; these changes do **not** claim exact screenshot parity. The form only displays receipt confirmation on a persisted `202` response, not on click. Payment method is an **unpaid preference**, not a Payabl transaction. A pending application is **not** a roster player, active membership, invoice or child/guardian app account.

## Security model and activation prerequisites

The public POST is `/api/public/academy/registration`, reached via a SvelteKit same-origin proxy, and is **disabled by default**. There are no preconfigured secrets or public grants. Until explicitly enabled and verified in isolated staging, submit returns a real error, and the UI must not say the registration was accepted.

The server validates the same Academy-only organization for every submission, rejects duplicate/unknown JSON fields, bounds payloads, validates required contacts, dates, consent choices and signature, requires a configured allowed origin, and uses a trusted-network fingerprint for durable rate limiting. The private medical/signature portion is sealed with AES-256-GCM and associated with the server-generated registration UUID; append-only consent records retain a policy version and evidence hashes. The RLS-enabled tables and the SQL functions are default-deny to the ordinary runtime role; only narrow operator-reviewed `EXECUTE` grants may be added.

**Do not activate until all of these are satisfied**:

- Legal review and approved **Greek** policy text for the exact consent statements/age groups, retention and data subject requests, plus approved policy version. Do not treat the current copy as legal sign-off.
- An independently managed 32-byte AES evidence key, stable key ID, tested key backup/recovery and rotation/revocation protocol. Do not store keys or decrypted child data in code, frontend, logs, screenshots, PR descriptions, or sample fixtures.
- Verified trusted reverse-proxy remote-address handling: the SvelteKit server-to-API hop currently appears as the proxy address unless infrastructure supplies a reviewed trustworthy fingerprint mechanism. **Do not activate with a shared proxy IP**: multiple families would otherwise share the rate-limit bucket. Never trust arbitrary browser-supplied IP headers.
- Designated purpose-limited runtime DB identity and reviewed privilege separation, DB migration rehearsals, DPA/privacy authorization and operator retention policies. Grant the runtime role `USAGE` on `academy_intake` plus `EXECUTE` only on `academy_intake.submit_public_registration(uuid,jsonb,bytea,text,text,text,text)`; grant the authorized admin runtime role `EXECUTE` on `academy_intake.read_admin_registration_queue(text,integer,integer)` only. **Do not grant table SELECT/INSERT to public or API roles.**
- Read-only intake review is limited both in the HTTP endpoint **and** database function to verified Academy super admins. Raw sealed medical evidence must never be exposed in this list. Existing `/api/admin/academy/office/registrations` remains a distinct workflow; there is no automatic cross-table transfer, approval, child account or charge.
- Real PostgreSQL integration coverage for normal submission, repeated/invalid consent, missing or malformed evidence, concurrency/rate limiting, rollback, unauthorized read, older policy values, privacy/retention behaviour, and a full browser screenshot/regression audit against approved references.
- Staging-only configuration: `AcademyRegistration__Enabled=true`, `AcademyRegistration__AllowedOrigin`, `AcademyRegistration__FingerprintKey`, `AcademyRegistration__EvidenceKey`, `AcademyRegistration__EvidenceKeyId`, `AcademyRegistration__PolicyVersion`. Never copy staging credentials into Hostinger production. Keep `Enabled` false where tests and policy review are not complete.

## Status

The five-step frontend, backend API gate, private transactional SQL function, read-only super-admin intake preview, and source/contract tests are implementation work; **not approval to release/enable public registrations**. The original Lefteria-FC repository and live Hostinger production are untouched.
