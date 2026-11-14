/* Installation support only. No private pages, API data or credentials are
   cached. Existing push subscriptions stay on /mobile/push-service-worker.js. */
self.addEventListener('fetch', event => {
  if (event.request.mode !== 'navigate') return;
  event.respondWith(fetch(event.request).catch(() => new Response(
    '<!doctype html><html lang="el"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Λευτεριά FC · Εκτός σύνδεσης</title><body><h1>Δεν υπάρχει σύνδεση</h1><p>Συνδέσου στο διαδίκτυο για να ανοίξεις την εφαρμογή.</p><a href="/app">Δοκίμασε ξανά</a></body></html>',
    { status: 503, headers: { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store', 'Content-Security-Policy': "default-src 'none'; base-uri 'none'; frame-ancestors 'none'" } }
  )));
});
