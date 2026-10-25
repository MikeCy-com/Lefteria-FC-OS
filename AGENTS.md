# Lefteria FC OS rewrite engineering guide

## Source of truth

- This repository is the rewrite implementation target.
- For current behavior, inspect MikeCy-com/Lefteria-FC on GitHub at the recorded commit or a newer explicitly recorded main commit. A local copy may be secondary context, never proof of production state.
- GitHub main is a source baseline; deployed revision, VPS state, live data and external credentials are not verified here.
- Preserve user changes and work on a dedicated branch. Keep the old application and data authoritative during migration.

## Mission

Build a modular, maintainable and secure club operations platform on the approved target stack. Start as a modular monolith. Preserve existing workflows, data, users, roles, privacy rules, integrations, mobile/PWA paths, public URLs and deployment compatibility unless a reviewed migration explicitly changes them.

Work in small, reviewable vertical slices. Map old routes, features, collections, roles and integrations before replacing behavior. Do not build mock screens in place of real workflows.

## Security and data

- Never commit credentials, tokens, private keys, production data or service-account files to source, images, fixtures or documentation. Use environment configuration or an approved secret store.
- Enforce authorization server-side on every private operation and scope each query to the authenticated user's organization/team/group and record ownership. Frontend guards are not security controls.
- Default to deny. Preserve or strengthen OTP/login rate limits, safe authentication errors, youth privacy, validated uploads, audit records and secret-safe logging.
- Preserve VAPID keys and subscription contracts during transition; rotating the key invalidates existing push subscriptions.
- Keep PostgreSQL, API and workers on private Compose networks. Expose only the reviewed edge/reverse proxy. Never publish database ports.
- Every schema change requires a versioned migration, data validation, recovery approach and tested rollback or forward recovery.
- Never call live payment, SMS, social-publishing or backup providers from tests or unapproved tooling.

Old production security policy: https://github.com/MikeCy-com/Lefteria-FC/blob/5111f06af5d8b17f855ceed9637caca02db117e2/memory/SECURITY.md

## Engineering workflow

Inspect current implementation and callers on GitHub before changing a module. Record behavior, authorization, compatibility and acceptance criteria. Follow ADRs; update them when evidence changes a decision.

Use documented commands and report exact checks. Test authorization, scope boundaries, migrations and provider adapters with safe fakes. Integration tests use isolated services/data.

Do not commit, push, merge, release, deploy, change production infrastructure or alter production data without explicit authorization.
