# Persistent public news URL registry (foundation only)

SEO-02 / issue #446. Existing published news routes still use their current legacy title-plus-ID URLs. This migration introduces owner-only reservations; no backfill, route change, public resolver, admin editor, or new grant is activated.

- One globally unique ASCII slug reservation maps to one exact existing imported public news edition (organization plus immutable article ID). A canonical pointer is constrained to an alias of the same edition. Shared news can have distinct editions.
- Owner-only invoker function validates syntax, reserved route names, readiness, exact published document ownership and legacy raw-ID conflicts. Claims serialize and require the expected current slug. Rename retains old reservations; another edition cannot acquire them. A prior alias may become canonical again for the same edition.
- No anonymous/app/admin runtime role is granted table or function access. RLS without policies adds defense in depth. No public metadata or private source data is copied.
- Down refuses deletion when reservations exist. Sequential tests run in rollback-only disposable DB transactions; two independent committed fixture claims test cross-owner concurrency and clean up their random reservation. Both runtime roles are checked for direct table and function denial.

Remaining: explicit reviewed slug allocation/transliteration and collision suffix policy; inventory existing raw IDs and aliases, future import collision guard, native draft publication integration, authorized per-organization editor with audit and compare-and-swap, read-only verified-public resolver, noindex for private/youth content, legacy 301s to ID-free routes, sitemap and canonical integration, concurrent rename and real admin permission tests, staging migration/rollback rehearsal. Do not populate the registry or switch URLs before these gates pass. This is not a completed clean URL feature.
