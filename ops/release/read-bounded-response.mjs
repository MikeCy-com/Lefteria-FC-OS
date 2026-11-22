/**
 * Bounded, fail-closed staging HTTP body reader. Used only on pre-approved
 * read-only GET responses; do not log returned bytes or private body content.
 */
export async function readBoundedResponse(response,maxBytes){
  if(!Number.isSafeInteger(maxBytes)||maxBytes<1||maxBytes>1024*1024)
    throw new Error('Invalid staging response limit.');
  const declared=response?.headers?.get?.('content-length');
  if(declared!==null&&declared!==undefined&&
     (!/^(?:0|[1-9][0-9]*)$/.test(declared)||
      !Number.isSafeInteger(Number(declared))||
      Number(declared)>maxBytes))
    throw new Error('Staging response size refused.');
  if(!response?.body||typeof response.body.getReader!=='function')
    throw new Error('Staging response body unavailable.');
  const reader=response.body.getReader();
  const chunks=[];
  let size=0;
  try{
    while(true){
      const {done,value}=await reader.read();
      if(done)break;
      if(!(value instanceof Uint8Array))
        throw new Error('Unexpected staging response chunk.');
      size+=value.byteLength;
      if(size>maxBytes)
        throw new Error('Staging response exceeded size limit.');
      chunks.push(value);
    }
  }catch{
    try{await reader.cancel();}catch{}
    throw new Error('Staging response body rejected.');
  }finally{
    reader.releaseLock();
  }
  const bytes=new Uint8Array(size);
  let offset=0;
  for(const chunk of chunks){bytes.set(chunk,offset);offset+=chunk.byteLength;}
  return new TextDecoder('utf-8',{fatal:true}).decode(bytes);
}
