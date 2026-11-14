# Founding web admin

Use first-time setup on a fresh OS database with web admin authentication enabled and the dedicated web-admin database role provisioned. Generate a random 32-byte setup code encoded as 64 lowercase hexadecimal characters. Set `WebAdmin__FoundingSetupCode` through the deployment's secret configuration. Compose uses `WEB_ADMIN_FOUNDING_SETUP_CODE` and the isolated preview uses `PREVIEW_ADMIN_FOUNDING_SETUP_CODE`.

Open `/admin/login` on the configured HTTPS origin and choose the owner setup option. Enter the setup code and a unique password of at least 12 characters and at most 72 UTF-8 bytes. The resulting account is a super-admin for both the club and Academy. Remove the setup secret from deployment configuration after successful setup.

Any existing admin record closes first-time setup, including an imported admin record. Setup never upgrades or replaces such an account. For a founding account, the database blocks deletion, demotion, deactivation, and removal of its two organization grants. Application roles cannot update account credentials directly. For lost credentials, a trusted database operator can replace only the founding account's password hash by using the account ID from `identity.founding_admin` and the project's supported bcrypt format. Existing sessions fail credential-digest validation after password rotation. Use a forward migration after setup to preserve ownership protections.

The disposable CI database checks the fresh-install path, existing-account behavior, replay denial, and protected mutations. Treat live operations as a separate deployment decision.
