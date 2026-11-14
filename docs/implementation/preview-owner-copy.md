# Preview-only owner access recovery

This operator tool handles one reviewed failure: an archived active super-admin was
quarantined solely because its staff reference does not exist. The source has
two-factor authentication enabled, but the isolated OS preview cannot currently
complete that second factor.

**This is an explicitly approved password-only testing copy, not a repair of the
original account.** The copy retains the archived password hash and username.
That makes the existing password usable in the isolated preview with weaker
authentication. Obtain the owner's explicit approval for that exact change
before committing it. Do not use this operation for production.

The tool creates a new UUID, the original legacy-super-admin default grants for
club and academy, and one security audit event. It creates no legacy mapping,
staff assignment, mobile binding or founding-owner marker. It does not alter the
original account source, quarantine issue, existing staff account, password hash,
or any production service. The copy's 2FA flag is false; the archived source's flag
remains true.

## Boundaries

- PostgreSQL database must be exactly `lefteria_os_preview`, independently verified
  on the isolated preview container. A database name alone is not host evidence.
- Connect as the actual owner of the admin and source tables.
- Confirm the exact import digest, source-row UTF-8 SHA256, source UUID, username,
  and email. No password or credential hash is an input or output.
- Only an active `super_admin` with source 2FA enabled, exactly one
  `admin_staff_reference` quarantine issue, a nonexistent staff record, no reverse
  staff pointer, a CY eight-digit phone, and absent explicit organization fields
  qualifies. Explicit/empty organization scope and additional issues fail closed.
- Existing accounts, duplicate source usernames, prior mappings and repeated
  recovery attempts fail closed. No account is overwritten.
- The helper exists only in `pg_temp` for the operator session. It is not a
  migration, public API, runtime privilege, or first-time-setup bypass.

## Review and execution

Take a protected backup of the isolated preview database first. Review the
archive's nonsecret identity, status, organization and staff fields. Obtain
`expected_source_sha256` using
`encode(sha256(convert_to(source_json,'UTF8')),'hex')` for the exact archived row.
Never print or export the full `source_json`: it contains credential material.

Use the exact reviewed revision of both SQL files. Through the verified preview
PostgreSQL container, run the wrapper with these psql arguments (values shown are
placeholders, not accounts or credentials):

```text
psql -X -v ON_ERROR_STOP=1 -d lefteria_os_preview \
  -v expected_database=lefteria_os_preview \
  -v expected_import_sha256=<reviewed-import-digest> \
  -v source_legacy_id=<reviewed-source-uuid> \
  -v expected_source_sha256=<reviewed-source-row-digest> \
  -v expected_username=<exact-source-username> \
  -v expected_email=<exact-source-email> \
  -v approve_password_only=true \
  -f prepare-preview-owner-copy.sql
```

Default execution prepares the complete account, grants and audit within one
transaction, then **rolls everything back**. It does not enable access. After
reviewing the dry-run result and obtaining the action-time approval for the
testing-only access change, add `-v apply_recovery=true` to commit. Quote values
correctly for the operator's shell; do not paste passwords into commands.

On success, verify the copy's role/grants, that its source remains quarantined
with 2FA enabled, and that the existing staff account is unchanged. The owner
then signs in at the preview's `/admin/login` using the exact original username
and password; no credentials are needed in chat. No SMS or other provider is used.

The audit action is `admin.preview_password_copy`, reason
`approved_preview_password_only`. Its subject digest binds the import digest,
source-row digest and source UUID. Its actor UUID identifies the testing copy.

This forward recovery deliberately does not rewrite initial-import history.
The initial-only account importer is not a merge or recovery tool and must not
be rerun to force reconciliation after this approved separate identity exists.
Future production migration must preserve source 2FA and use supported second
factor authentication.

## Verification

`preview-owner-copy-checks.sql` requires a fresh disposable database with the
same exact preview name, uses synthetic identities, and rolls back every fixture.
CI applies actual migrations to that separate disposable database, tests wrong
database/approval/digest/role/issue/scope/staff-reference refusals, transaction
rollback, copy/grant correctness, source/staff preservation and collision refusal,
then removes only that CI database. It never connects to a hosted preview.
