// Web Push is an explicit member/device opt-in. It does not cache bearer
// tokens or private announcement content in the service worker.
export type DevicePushConfig={
  enabled:boolean;public_key:string|null;active_devices:number
};
export function validDevicePushConfig(raw:unknown):raw is DevicePushConfig {
  return !!raw&&typeof raw==='object'&&!Array.isArray(raw)&&
    'enabled' in raw&&typeof raw.enabled==='boolean'&&
    'public_key' in raw&&(raw.public_key===null||
      typeof raw.public_key==='string'&&/^[A-Za-z0-9_-]{80,128}$/.test(raw.public_key))&&
    'active_devices' in raw&&Number.isSafeInteger(raw.active_devices)&&
    (raw.active_devices as number)>=0&&(raw.active_devices as number)<=3&&
    (!raw.enabled||typeof raw.public_key==='string');
}
export function hasDevicePushSupport():boolean{
  return typeof window!=='undefined'&&window.isSecureContext&&
    'serviceWorker' in navigator&&'PushManager' in window&&
    'Notification' in window;
}
function decodeBase64Url(value:string):Uint8Array<ArrayBuffer> {
  if(!/^[A-Za-z0-9_-]+={0,2}$/.test(value))throw new Error('Invalid device push key.');
  const text=atob(value.replace(/-/g,'+').replace(/_/g,'/')
    .padEnd(Math.ceil(value.length/4)*4,'='));
  const result=new Uint8Array(new ArrayBuffer(text.length));
  for(let i=0;i<text.length;i++)result[i]=text.charCodeAt(i);
  return result;
}
function encodeKey(key:ArrayBuffer|null):string {
  if(!key)throw new Error('This browser did not supply its push key.');
  const bytes=new Uint8Array(key);
  let text='';
  for(const byte of bytes)text+=String.fromCharCode(byte);
  return btoa(text).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'');
}
export async function browserPushSubscription():Promise<PushSubscription|null> {
  if(!hasDevicePushSupport())return null;
  const reg=await navigator.serviceWorker.getRegistration('/mobile/');
  return reg?reg.pushManager.getSubscription():null;
}
export async function enableBrowserPush(publicKey:string,
  persist:(endpoint:string,p256dh:string,auth:string)=>Promise<boolean>):Promise<boolean> {
  if(!hasDevicePushSupport())throw new Error('Η συσκευή δεν υποστηρίζει ειδοποιήσεις.');
  const key=decodeBase64Url(publicKey);
  if(key.length!==65||key[0]!==4)throw new Error('Μη έγκυρο δημόσιο κλειδί ειδοποιήσεων.');
  // Must be called directly from an explicit user click (no automatic
  // permission prompts or silent subscription on app startup).
  const permission=await Notification.requestPermission();
  if(permission!=='granted')throw new Error('Δεν επιτράπηκαν οι ειδοποιήσεις συσκευής.');
  const reg=await navigator.serviceWorker.register(
    '/mobile/push-service-worker.js',{scope:'/mobile/',updateViaCache:'none'});
  const existing=await reg.pushManager.getSubscription();
  // A browser profile may have changed from one mobile identity to another.
  // Rotate its endpoint on explicit opt-in rather than assuming that the
  // previous subscription belongs to this role/identity.
  if(existing)await existing.unsubscribe();
  const current=await reg.pushManager.subscribe({
    userVisibleOnly:true,applicationServerKey:key
  });
  try{
    const auth=encodeKey(current.getKey('auth'));
    const p256dh=encodeKey(current.getKey('p256dh'));
    if(!current.endpoint||!await persist(current.endpoint,p256dh,auth))
      throw new Error('Η συσκευή δεν επιβεβαιώθηκε από τον εξυπηρετητή.');
    return true;
  }catch(error){
    await current.unsubscribe().catch(()=>false);
    throw error;
  }
}
export async function disableBrowserPush(
  remove:(endpoint:string)=>Promise<boolean>):Promise<boolean> {
  const subscription=await browserPushSubscription();
  if(!subscription)return true;
  if(!await remove(subscription.endpoint))
    throw new Error('Δεν επιβεβαιώθηκε η διαγραφή της συσκευής.');
  return subscription.unsubscribe();
}
