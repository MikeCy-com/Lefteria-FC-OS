# Parent-to-player private document registration workflow

**PR #458: IMPLEMENTED IN CODE / STAGING NOT VERIFIED / PRODUCTION DISABLED.**
This is an extension of the already merged Academy registration, escrow, and
player-vault promotions. No real child files have been uploaded from Hostinger.

## 1–5 children, one guardian

- Each family can register one through five children (not a fixed three).
- The family form retains all identity, guardian, medical, consent, preferred
  kit name/number and payment-preference fields. Every child additionally chooses
  either a birth certificate, or **both** identity card sides with expiration.
- Per-child files are capped at 10 MB each and restricted to magic-byte-validated
  PDF/JPEG/PNG/WebP. Files are not published in public CMS or sent in Viber.
- The public registration handler submits each pending application atomically
  and, in the SAME transaction, records a unique digest of one independently
  generated 256-bit upload capability per child. All 1–5 submit or none do.
- Only after a commit-confirmed 202 with matching receipt/token arrays does the
  browser send each corresponding child's selected files over same-origin POST.
  The token is not a URL parameter, cookie, localStorage entry or logged text.
- On failure after successful registration, the confirmation **must not** say
  no registration was recorded: it shows the receipt, missing uploads and a
  retry action. A capability expires in one hour, so later failures require
  authorized staff to request a new private upload through the existing
  player Documents management workflow.

## Data and permission boundary

1. The anonymous upload is allowed only when the configured registration intake
   is enabled, the origin and trusted proxy fingerprint are validated, the
   independent document vault key is present and the exact registration's
   upload digest remains valid for **one hour**.
2. PostgreSQL independently rejects an unknown token, cross-child ID, wrong
   organization, already reviewed application, expired capability, conflicting
   slot, invalid type/size and applications already placed on a team.
3. AES-256-GCM encrypted ciphertext, nonce, auth tag, SHA-256 digest and key ID
   are stored in private, RLS-protected `academy_intake.registration_document_escrow`.
   Default privileges grant NOTHING to public or normal application roles.
4. Only the authorized, audited roster-placement INSERT can promote that
   registration's encrypted files into
   `academy_private.encrypted_document_objects` and
   `academy_private.player_documents`. Audit and provenance link the original
   registration and approved player. Promoted escrow bytes are cleared. The
   destination is **that actual child's Player Documents**, never a family
   shared folder or public gallery.
5. Transferred objects remain `quarantined`; download, thumbnail,
   distribution and scan bypass remain disabled. Later secure decrypt adapters
   must derive AES-GCM associated data from the immutable promotion ledger:
   `lefteria_fc_academy:registration:<32-hex registration id>:<object uuid D>`.
   Do not assume the unrelated staff-upload player-ID AAD or re-label records.

## Mandatory isolated staging activation gate

- Confirm Hostinger testing database, media, secrets, TLS, reverse-proxy, ports
  and volumes are isolated from legacy/live production.
- Obtain reviewed Greek parent consent/retention policy and 32-byte
  `AcademyRegistration:EvidenceKey`, `DocumentVault:KeyBase64`, unique stable
  key IDs and independently tested backup/restore of encrypted data **and keys**.
- Verify `AcademyRegistration:FingerprintKey` is bound to the trusted client's
  actual address, not a shared untrusted X-Forwarded-For string.
- Explicitly GRANT EXECUTE to the isolated public-intake API role only on
  `academy_intake.submit_public_registration_with_upload` and
  `academy_intake.store_public_registration_document`; revoke that role's
  legacy seven-argument direct intake function grant. Never GRANT raw
  SELECT/INSERT/UPDATE on the escrow, token or player document tables.
- Set `AcademyRegistration:Enabled=true` ONLY in verified isolated staging,
  never production by implication.
- Test 1, 2, 4 and 5 synthetic children through browser and API, with mixed
  ID/birth uploads, replay, wrong sibling token, expired/revoked token,
  oversized file, invalid signature, concurrent slot collision, wrong
  origin, rejected application, cross-location manager access, client timeout
  and case of registration saved but attachments incomplete.
- Confirm scanner workflow, safe rejection, approved recovery, retention,
  erased/rejected applications, GDPR subject request procedure, malware
  handling and staff ability to retrieve quarantined scans **only after a
  separate explicitly authorized audited download adapter is implemented**.
- Verify the actual running staging SHA, migration, child Documents location,
  backup restoration and rollback plan. Never call a successful GitHub Actions
  job a completed Hostinger acceptance test.

**NOT implemented by #458:** malware scanner, audited download/review adapter,
automated retention purge or privacy DSAR workflow, Viber dispatch, or creation
of Academy invoices. These remain release blockers.

**ONE TEAM ● ONE DREAM**
