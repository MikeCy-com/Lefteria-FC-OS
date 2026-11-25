// A single scope-owned reader for server-authoritative notification counts.
export function createNotificationBadgeRefresher(
  read:()=>Promise<number>,publish:(count:number)=>void
){
  let active=true,running=false,queued=false;
  async function refresh(){
    if(!active)return;
    if(running){queued=true;return;}
    running=true;
    try{
      do{
        queued=false;
        let count=0;
        try{
          const result=await read();
          if(Number.isSafeInteger(result)&&result>=0&&result<=999)count=result;
        }catch{/* Never keep a count from a failed authorization/read. */}
        // A read receipt or focus event may invalidate this snapshot while
        // the request is pending. Only publish the latest settled response.
        if(active&&!queued)publish(count);
      }while(active&&queued);
    }finally{running=false;}
  }
  function dispose(){active=false;queued=false;}
  return {refresh,dispose};
}
