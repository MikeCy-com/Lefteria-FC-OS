# Academy group publication: private until explicit approval

A newly created Academy group is **not** automatically ready for
public viewing. The historical `club.academy_group_profiles.is_public`
column was added with `DEFAULT true`, which could put a newly
created profile on the public Academy page before an authorized
manager reviewed its age range, schedule and display information.

The new migration changes the **default for future profile INSERTs**
to `false`. It does not rewrite the is_public value of **any existing
profile**; previously published groups remain visible exactly as before.
The migration's rollback restores the previous *default* only, without
overriding publication decisions or historical rows.

The existing audited, optimistic concurrency protected
`PUT /api/admin/football/academy-group-profiles/{groupId}/visibility`
endpoint remains the only administrative publication/withdrawal action.
PR #392 ensures it also requires a current exact city or location grant.
An explicitly approved group can enter
`club.read_public_academy_group_directory()` only when its site and
city are active, and a later withdrawal removes it from the directory.

Disposable PostgreSQL HTTP checks now create an Academy group through
the real location-scoped API, verify `is_public=false` and no anonymous
listing, approve visibility via the verified manager endpoint, confirm
public listing, withdraw it, then check public disappearance. Existing
group records/visibility are never mass-updated.

No automatic notifications or production deployment. Live Hostinger
remains untouched.
