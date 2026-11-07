# Academy enquiry HTTP release gate

The original Lefteria FC Academy `/join` still does **not** accept public lead submissions in the OS rewrite. The current foundations are separately reviewed: input/consent policy, private lead table, rate bucket, HMAC fingerprint, atomic function, and a database adapter which fails closed until explicit execution grants.

`AcademyEnquiryActivationGate.TryCreateFingerprint` is an **unconnected policy**, not a route switch. A future endpoint must explicitly check a feature setting set to the exact string `true`, load a **dedicated** 32-byte base64 HMAC secret from approved secret management, and receive an `IPAddress` from a connection whose reverse-proxy trust configuration has been audited. Do **not** use `X-Forwarded-For` directly as an identity or trust all proxies. Invalid/missing key, disabled setting and missing remote address all deny activation.

The following items are prerequisites to mapping any public POST endpoint:

1. Verify the Hostinger/Traefik trusted-forwarded-headers configuration against the actual deployment path, and test spoofed headers, direct traffic and proxy chains.
2. Approve Academy-specific retention, guardian consent, contact-data minimization and abuse controls.
3. Restrict `marketing.store_academy_lead` execution to the least-privileged runtime role with no direct lead/bucket table grants, validate SQL owner and function security, and run real PostgreSQL denial, concurrency, throttle, rollback and audit tests.
4. Register only an explicitly opted-in, bounded public API contract with no success unless the committed DB write returns `stored`. Ensure rate-limit responses do not reveal stored contact information and no SMS is initiated.
5. Update versioned OpenAPI and generated Svelte client, build an accessible real form, test network errors, privacy notices, consent and partner attribution.
6. Pass all five Rewrite build jobs on the reviewed branch, then separately authorize any **isolated preview** release after snapshot/rollback validation.

Never modify the original `MikeCy-com/Lefteria-FC` or the live `lefteriafc.cy` production system. Do not add credentials to source or PR comments.
