/* Lefteria FC OS — generic push wake-up, no personal or club content.
   A push gateway or notification surface must never receive member names,
   roster IDs, announcement titles, bodies, file URLs or mobile tokens. */
self.addEventListener('push',event=>{
  event.waitUntil(self.registration.showNotification('Λευτεριά FC',{
    body:'Υπάρχει νέα ενημέρωση για την ομάδα σου.',
    tag:'lefteria-official-announcement',
    icon:'/mobile/club-logo.png',
    badge:'/mobile/club-logo.png',
    renotify:false,
    data:{url:'/app?tab=notifications'}
  }));
});
self.addEventListener('notificationclick',event=>{
  event.notification.close();
  event.waitUntil(self.clients.openWindow('/app?tab=notifications'));
});
