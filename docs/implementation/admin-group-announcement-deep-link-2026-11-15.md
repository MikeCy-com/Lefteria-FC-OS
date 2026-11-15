# Contextual app announcements from First Team and Academy groups

On /admin/groups, a manager or Super Admin with the group's existing
creation/write permission can open **Ανακοινώσεις εφαρμογής** for the
selected verified group. The link carries its organization and canonical group
ID. The separate /admin/announcements page may use that ID only after the
current WebAdmin session's organization grant is checked and the *fresh*
/api/admin/football/audiences result confirms that the group is present in
that exact organization.

The editor retains its existing draft save, private media attachment, publish,
readership and withdrawal methods. No additional notification send, mobile
messaging, access grant, public CMS publish or app recipient copy is added.
A return link goes to the original verified group. Invalid/stale/cross-org
deep links are not automatically selected. Async loads are aborted or discarded
on group/owner/session changes.

This is a usability improvement, **not** sufficient proof that managers are
restricted to an Academy location: the underlying legacy announcements SQL
currently authorizes at organization level. The complete SQL-side
city/location/team guard for listing, drafts, media, publish, readers and
withdrawal remains required by issue #361. Never regard a hidden UI option
as an authorization boundary.

Validate Svelte SSR, live API permission/HTTP cases, disposable PostgreSQL,
OpenAPI/edge and browser CI before merge. Do not deploy live Hostinger.
