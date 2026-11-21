# MVP payment-provider configuration wiring — sandbox only

**Status:** configuration-readiness slice, no payments enabled and no Hostinger deployment.

## Corrected integration

ASP.NET Core already binds two independently validated options:
`PayablOptions` from `Payabl__*`, and `TwilioPaymentSmsOptions` from
`TwilioPaymentSms__*`. The documented root `.env.example` settings existed,
but the API service in the production-shaped root `compose.yaml` did not
forward them, so an operator setting the documented sandbox credentials would
still see Payabl unavailable. The SMS checkout link integration had the same
missing runtime binding.

The Compose API environment now forwards exactly these disabled-by-default
variables, with no hardcoded secrets:

- `Payabl__Enabled=false`, `Payabl__Sandbox=true`,
  `Payabl__MerchantId`, `Payabl__Secret`, `Payabl__PublicOrigin`
- `TwilioPaymentSms__Enabled=false`,
  `TwilioPaymentSms__AccountSid`, `TwilioPaymentSms__AuthToken`,
  `TwilioPaymentSms__MessagingServiceSid`

The existing backend `ValidateOnStart()` still prevents unsupported merchant
profiles: a Payabl connection is sandbox-only, requires a bare HTTPS callback
origin and restricted merchant ID/signature secret; Twilio checkout SMS requires
an approved service SID and account token. API calls only use the existing
signed, nonce-bound webhook workflow and explicit admin action. The gateway
adapter accepts **no production merchant/checkout endpoints**.

## Additional preflight security

`deploy/production/verify-compose-security.py` now checks the resolved
Compose service configuration without logging credentials. It fails if
payment settings are dropped, the sandbox flag is false (even while disabled),
an active Payabl callback origin differs from the site's public HTTPS origin,
credentials are missing for an explicitly enabled sandbox merchant, or SMS
payment links are enabled without the Payabl sandbox. Pure negative Python tests
and the real Docker Compose configuration check run in GitHub CI.

## Operator-controlled staged activation (not performed)

1. Obtain written sandbox merchant authorization, approved card/3DS test
   accounts, HTTPS test callback and payment-link SMS service details.
2. Set **only** isolated sandbox environment credentials outside Git;
   maintain `Payabl__Sandbox=true` and distinct merchant/Twilio secrets.
   Confirm `Payabl__PublicOrigin` exactly matches the tested
   `PUBLIC_ORIGIN` with no pathname or query. Real provider credentials must
   never enter GitHub logs, user browsers, mobile binaries, public assets or
   database snapshots.
3. Independently audit the existing signed provider callback, status enquiry,
   exact amount/order and settlement evidence, subscription saved-card consent,
   refunds, reconciliation and Cyprus invoice/tax requirements.
4. Verify manual card checkout and failure/retry in a **separate** authorized
   sandbox, with no original-site financial data. Verify the SMS endpoint's
   per-message operator approval and delivered-message events.
5. Only after all exact-SHA go-live gates pass, request a separate explicit
   production cutover decision. Do not enable real merchant processing or
   place live Payabl/Twilio secrets in these sandbox settings.

**Not changed:** protected preview Compose, payment capture state, direct
collection, saved-card tokens, webhooks, JWT, monthly invoice allocation,
live domain, Hostinger containers, application deployment or any provider
credential. The existing live merchant/financial release gates remain blocked.
