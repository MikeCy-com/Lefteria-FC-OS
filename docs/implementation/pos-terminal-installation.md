# OS POS terminal installation

Open **Administration → POS · Ταμεία, βάρδιες & προϊόντα → Εφαρμογή ταμείου · Lefteria POS → Άνοιγμα / εγκατάσταση POS**. A cashier may also access the link from Το ταμείο μου. Copy the same-origin link for the assigned device, never credentials.

The dedicated path is `/admin/pos/terminal`. It uses a separate manifest ID and start URL, and the existing OS online-only cashier login/session, register assignment, fiscal policy checks and settlement UI. It is not the legacy location/code login, an APK, an EXE, an offline transaction queue, or permission to collect money. Installing does not grant access. Logout before switching to a cashier account on a shared browser. Existing member app manifest and navigation worker remain unchanged.

On compatible Android/desktop browsers, use the install prompt or browser menu; on iOS Safari use Add to Home Screen. Actual installation depends on HTTPS and browser support; verify on staging devices before release. No production changes.
