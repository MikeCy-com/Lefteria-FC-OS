# Mobile phone authentication application workflow

Source contract: MikeCy-com/Lefteria-FC at GitHub commit
5111f06af5d8b17f855ceed9637caca02db117e2, backend/routes/mobile_auth.py,
backend/models.py and backend/profile_media.py. This project has no HTTP, EF or
provider dependency. The API maps typed faults and publishes the DTOs directly.

## Boundary and response contracts

- Request OTP returns message, phone, nullable role_detected and expires_in=300.
- Fan signup accepts first_name/last_name or the legacy name fallback, phone and
  ISO yyyy-MM-dd birthday. Successful signup returns phone, role_detected=fan
  and simulated=false. It never returns a debug code. Names are limited to 100
  characters each, control characters are rejected, and birthday cannot be future.
- Verification returns either MobileRolePickerResult (multi_role=true,
  identities and phone, no token) or MobileLoginResult (token, user, role and
  available_roles). Both are registered as derived JSON contracts.
- The HTTP request mapper must distinguish omitted remember_days from explicit
  null using RememberDeviceSelection. Omitted mobile requests retain 90 days;
  explicit null means 24 hours; provided integer values must be 30, 60 or 90.
  The mapper must reject JSON strings, booleans and floating-point values.
- Same-role switching returns token:null, noop:true and role. Other switches
  return token/user/role/available_roles and preserve the original exact expiry.
- Own-profile results include available_roles and current_role_available.
  A revoked current identity can expose its own profile metadata, but all
  operational links are redacted. Explicit switching to its existing active fan
  identity remains possible; another formal role cannot be selected from that
  revoked session.
- Organization context contains organization_id/name, available_organizations
  and requires_organization_selection. It contains no operational data. Management
  identities require an explicit active admin FK. Empty grants deny access;
  multiple grants without active selection require a choice.
  A sole organization is persisted through the same atomic selection operation
  when the shared active context differs, retaining the source's durable behavior.

PUT /api/mobile/profile returns the updated own MobileUserDto directly. Its
allowlist is name, email, first_name, last_name, birthday, favourite_player_id and
notification_prefs. Null values are ignored, and a request with no accepted
non-null fields returns 400. An explicit name wins; otherwise a first/last-name
change derives the display name from the changed part and current other part
when the combined value is nonempty. An empty first/last part is accepted.

New profile writes enforce display name 1..200 characters, first/last names up to
100, plain email up to 254 without display names, and opaque favourite-player IDs
up to 128. Name and preference-ID controls are rejected. Empty email/player-ID
strings clear those values. Birthday must be a nonfuture ISO date. This
self-reported date does not establish verified adulthood or alter social/youth
authorization. The roster picker remains queued; a favourite-player ID grants no
player access and does not become an operational link.

Notification preferences replace the complete object, including an empty object
to clear it. The typed HTTP boundary accepts booleans only, up to 64 unknown keys
for forward compatibility, each 1..64 characters without controls. This tightens
the source's truthiness coercion. All accepted full-profile writes use a locked,
freshly active own identity and append mobile.profile.update / own_profile audit
with the same UTC UpdatedAt in one transaction. Separate roles sharing a phone
retain independent profile fields. Unknown body identifiers, role, phone, links
and organization fields are ignored. Profile JSON is limited to 16 KiB; the
future avatar multipart route is separate.

PhoneNumber stores canonical ASCII digits and emits a '+' wire value. It accepts
space, hyphen and parentheses, optional leading '+' or '00', prefixes 357 for an
eight-digit local number, and enforces 8..15 final international digits. It
rejects controls, letters, Unicode digit lookalikes, repeated prefixes and an
international number beginning with zero. Matching always uses the full number.

## Repository and provider obligations

The repository must lock the OTP row and relevant identity/grant rows until
commit. Acquisition order must be deterministic. Persist verification calls and
consumption with the same transaction as fan changes, login metadata and audit.
Disposal without CommitAsync rolls back. Unknown valid proofs are consumed before
404; invalid selected roles retain the proof while spending one verification call.
Role-picker verification retains the challenge and spends one of five calls.

Fan signup stages profile fields on the hashed challenge. Only a successful OTP
comparison can create/update the fan. Verified formal-role phones without a fan
receive a separate neutral fan identity named 'Φίλος της Λευτεριάς'; do not copy a
child's formal display name into this public identity. Existing disabled fans
remain disabled. Existing fan preferences/name are preserved by the neutral ensure.

IOtpProtector generates a cryptographic six-digit code and a keyed digest bound
to challenge ID and full phone. Store only the digest. SMS delivery occurs after
atomic challenge issue, outside all application DB transactions. Delivery failure
deletes only that challenge ID, so a slow failed request cannot delete a newer OTP.
Production delivery failure is an error; no simulated authentication path exists.

IMobileTokenIssuer signs a validated MobileSession without changing device ID or
expiry. Its normalized phone is digits; adapters may serialize the JWT phone claim
using PhoneNumber.WireValue. JWT signature/issuer/audience/type validation happens
before the application receives a session. Neither browser role lists nor legacy
alias IDs are trusted grants. Old-token acceptance requires an explicit migration
bridge and is not implicit in these interfaces.

AuthAuditEntry contains IDs and machine reason codes, never phone, OTP, name,
password, token or provider response bodies. Organization selection must re-read
current grants, persist only a selected authorized organization, and append audit
atomically. IAuthThrottle must use shared persistent state and an API-supplied
trusted client key; it must not trust arbitrary forwarded headers.
OTP request and fan-signup delivery deliberately tighten the source limit by
acquiring two shared request buckets: ip:<trusted-client-key> and
phone:<canonical-full-digits>. The same configured request allowance applies to
both buckets, preventing distributed SMS abuse against one destination. The phone
key is transient and must be hashed before persistence. Verification retains its
existing per-client scope/limit and challenge call allowance.

## Fault mapping

BadRequest=400, Unauthorized=401, Forbidden=403, NotFound=404, Validation=422,
TooManyRequests=429, ServiceUnavailable=503. Return Detail as the response detail
field. Set Retry-After when RetryAfterSeconds exists. Preserve cancellation and
map unexpected infrastructure faults to a sanitized server error.

Operational player/team/group links can remain empty until their actual relational
modules are migrated. Do not synthesize teams, children, staff assignments or
financial data to make the login workflow appear complete.
