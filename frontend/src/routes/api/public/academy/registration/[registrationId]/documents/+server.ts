import { INTERNAL_API_ORIGIN } from '$app/env/private';
import { json } from '@sveltejs/kit';
import type { RequestHandler } from './$types';

// Same-origin, strictly bounded binary relay. No cookies, Authorization,
// upload token, filename or child identity is logged, returned or cached.
export const POST: RequestHandler = async ({params,request,url}) => {
  const headers={'cache-control':'private, no-store',pragma:'no-cache'};
  if(request.headers.get('origin')!==url.origin)
    return json({code:'origin_denied'},{status:403,headers});
  if(!/^[0-9a-f]{32}$/.test(params.registrationId))
    return json({code:'invalid_registration'},{status:400,headers});
  const contentType=request.headers.get('content-type')??'';
  if(!contentType.startsWith('multipart/form-data; boundary=')||
     !contentType.slice('multipart/form-data; boundary='.length).trim()||
     contentType.length>200||/[\r\n]/.test(contentType))
    return json({code:'multipart_required'},{status:415,headers});
  const declaredLength=request.headers.get('content-length');
  if(declaredLength!==null){
    if(!/^(0|[1-9][0-9]*)$/.test(declaredLength))
      return json({code:'invalid_upload'},{status:400,headers});
    if(Number(declaredLength)>11*1024*1024)
      return json({code:'file_too_large'},{status:413,headers});
  }
  let apiOrigin:URL;
  try {
    apiOrigin=new URL(INTERNAL_API_ORIGIN);
    if(!['http:','https:'].includes(apiOrigin.protocol)||
      apiOrigin.username||apiOrigin.password||apiOrigin.pathname!=='/'||
      apiOrigin.search||apiOrigin.hash)
      throw Error('Invalid private upstream');
  }catch{
    return json({code:'upload_unavailable'},{status:503,headers});
  }
  const reader=request.body?.getReader();
  if(!reader)return json({code:'invalid_upload'},{status:400,headers});
  let size=0;
  const chunks:Uint8Array[]=[];
  try {
    while(true){
      const {done,value}=await reader.read();
      if(done)break;
      size+=value.byteLength;
      if(size>11*1024*1024)
        return json({code:'file_too_large'},{status:413,headers});
      chunks.push(value);
    }
    if(declaredLength!==null&&size!==Number(declaredLength))
      return json({code:'invalid_upload'},{status:400,headers});
    const body=new Uint8Array(size);
    let offset=0;
    for(const chunk of chunks){body.set(chunk,offset);offset+=chunk.byteLength;}
    const response=await fetch(new URL(
      '/api/public/academy/registration/'+params.registrationId+'/documents',apiOrigin),{
        method:'POST',credentials:'omit',cache:'no-store',redirect:'error',
        headers:{origin:url.origin,'content-type':contentType},
        body,signal:AbortSignal.timeout(30000)
      });
    if(response.status===202){
      const result:unknown=await response.json().catch(()=>null);
      if(result&&typeof result==='object'&&!Array.isArray(result)){
        const payload=result as Record<string,unknown>;
        if(['id_front','id_back','birth_certificate','passport_photo'].includes(
          payload.slot as string)&&payload.status==='quarantined'&&
          typeof payload.replay==='boolean')
          return json({slot:payload.slot,status:'quarantined',
            replay:payload.replay},{status:202,headers});
      }
    }
    const code=response.status===403?'upload_capability_invalid':
      response.status===409?'document_slot_conflict':
      response.status===413?'file_too_large':
      response.status===422?'invalid_upload':
      response.status===429?'upload_rate_limited':'upload_unavailable';
    const status=[400,403,409,413,415,422,429].includes(response.status)
      ?response.status:503;
    return json({code},{status,headers});
  }catch{
    return json({code:'upload_unavailable'},{status:503,headers});
  }
};
