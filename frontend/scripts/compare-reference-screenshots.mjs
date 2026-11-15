/**
 * Exact-viewport reference screenshot comparison for verified original UI
 * images. The reference PNGs are external, immutable evidence; never synthesize
 * them from the OS implementation and never mistake a 503 fixture for real data.
 *
 * Run with:
 *   LEFTERIA_UI_ARTIFACTS=/tmp/os-screens \
 *   LEFTERIA_REFERENCE_SCREENSHOT_DIR=/tmp/approved-original \
 *   LEFTERIA_REQUIRE_PIXEL_PARITY=1 node scripts/compare-reference-screenshots.mjs
 *
 * Identically named PNGs are expected in both directories. Reference images
 * must come from the same viewport/DPR, stable content and scroll state.
 * If the originals are absent, record UNVERIFIED instead of reporting success.
 */
import assert from 'node:assert/strict';
import { access, mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { chromium } from 'playwright';

const artifacts = process.env.LEFTERIA_UI_ARTIFACTS;
const references = process.env.LEFTERIA_REFERENCE_SCREENSHOT_DIR?.trim() || null;
const requireAll = process.env.LEFTERIA_REQUIRE_PIXEL_PARITY === '1';
const maxChanged = Number(process.env.LEFTERIA_MAX_PIXEL_CHANGED_RATIO ?? '0.02');
const colorTolerance = Number(process.env.LEFTERIA_PIXEL_COLOR_TOLERANCE ?? '12');
const selfTest = process.argv.includes('--self-test');
assert.ok(artifacts || selfTest, 'Set LEFTERIA_UI_ARTIFACTS to the isolated screenshot artifact directory');
assert.ok(Number.isFinite(maxChanged) && maxChanged >= 0 && maxChanged <= 1, 'Invalid pixel change threshold');
assert.ok(Number.isInteger(colorTolerance) && colorTolerance >= 0 && colorTolerance <= 255, 'Invalid RGB pixel tolerance');

const browser = await chromium.launch({headless:true});
let page;
async function comparePngBytes(referenceBytes, actualBytes) {
  page ??= await browser.newPage({viewport:{width:1280,height:900}});
  // Chromium natively decodes the true PNG bytes; no OCR, compression-based
  // similarity or resize that would hide major layout and text differences.
  return page.evaluate(async ({expected,actual,tolerance}) => {
    const open = base64 => new Promise((resolve,reject)=>{
      const image=new Image();
      image.onload=()=>resolve(image);
      image.onerror=()=>reject(new Error('PNG reference/capture could not be decoded'));
      image.src='data:image/png;base64,'+base64;
    });
    const [a,b] = await Promise.all([open(expected),open(actual)]);
    const width=Math.max(a.naturalWidth,b.naturalWidth);
    const height=Math.max(a.naturalHeight,b.naturalHeight);
    if(width<1||height<1||width>8192||height>50000)
      throw new Error('Screenshot dimensions exceed reviewed pixel budget');
    const chunkHeight=128;
    const left=document.createElement('canvas'),right=document.createElement('canvas');
    left.width=width;right.width=width;left.height=chunkHeight;right.height=chunkHeight;
    const lc=left.getContext('2d',{willReadFrequently:true}),rc=right.getContext('2d',{willReadFrequently:true});
    if(!lc||!rc) throw new Error('No Canvas 2D decoder');
    let changed=0,absoluteError=0,firstDifference=null;
    const total=width*height;
    for(let top=0;top<height;top+=chunkHeight){
      const h=Math.min(chunkHeight,height-top);
      const decode=(ctx,img)=>{
        ctx.clearRect(0,0,width,chunkHeight);
        if(top<img.naturalHeight) {
          const drawH=Math.min(h,img.naturalHeight-top);
          ctx.drawImage(img,0,top,img.naturalWidth,drawH,0,0,img.naturalWidth,drawH);
        }
        return ctx.getImageData(0,0,width,h).data;
      };
      const leftPixels=decode(lc,a),rightPixels=decode(rc,b);
      for(let index=0;index<leftPixels.length;index+=4){
        const alphaDifference=Math.abs(leftPixels[index+3]-rightPixels[index+3]);
        const rgbDifference=[
          Math.abs(leftPixels[index]-rightPixels[index]),
          Math.abs(leftPixels[index+1]-rightPixels[index+1]),
          Math.abs(leftPixels[index+2]-rightPixels[index+2])
        ];
        const missingPixel=leftPixels[index+3]===0 || rightPixels[index+3]===0;
        const isChanged=missingPixel
          ? (leftPixels[index+3]!==rightPixels[index+3])
          : (alphaDifference>tolerance || rgbDifference.some(delta=>delta>tolerance));
        if(isChanged){
          changed++;
          if(firstDifference===null) firstDifference={x:(index/4)%width,y:top+Math.floor(index/4/width)};
        }
        absoluteError+=missingPixel ? (isChanged?765:0) : rgbDifference[0]+rgbDifference[1]+rgbDifference[2];
      }
    }
    return {
      original:{width:a.naturalWidth,height:a.naturalHeight},
      os:{width:b.naturalWidth,height:b.naturalHeight},
      exactDimensions:a.naturalWidth===b.naturalWidth && a.naturalHeight===b.naturalHeight,
      pixels:total,
      changed,
      changedRatio:changed/total,
      meanAbsoluteRgb:Math.round(absoluteError/(total*3*255)*1000000)/1000000,
      firstDifference
    };
  },{expected:referenceBytes.toString('base64'),actual:actualBytes.toString('base64'),tolerance:colorTolerance});
}

async function doSelfTest(){
  page=await browser.newPage();
  const fixture=await page.evaluate(()=>{
    const canvas=document.createElement('canvas');
    canvas.width=6;canvas.height=4;
    const ctx=canvas.getContext('2d');
    ctx.fillStyle='#ffffff';ctx.fillRect(0,0,6,4);
    const a=canvas.toDataURL('image/png').split(',')[1];
    ctx.fillStyle='#000000';ctx.fillRect(2,1,1,1);
    const b=canvas.toDataURL('image/png').split(',')[1];
    return {a,b};
  });
  const original=Buffer.from(fixture.a,'base64'),changed=Buffer.from(fixture.b,'base64');
  const identical=await comparePngBytes(original,original);
  const mismatch=await comparePngBytes(original,changed);
  assert.equal(identical.changed,0);
  assert.equal(identical.exactDimensions,true);
  assert.equal(mismatch.changed,1);
  assert.equal(mismatch.pixels,24);
  assert.equal(mismatch.firstDifference.x,2);
  assert.equal(mismatch.firstDifference.y,1);
  assert.equal(mismatch.changedRatio,1/24);
  console.log('Chromium pixel comparator self-test passed: exact 1/24 changed pixel.');
}

try {
  if(selfTest) {
    await doSelfTest();
  } else {
    await mkdir(artifacts,{recursive:true});
    const reportPath=path.join(artifacts,'visual-pixel-comparison.json');
    const manifests=['results.json','role-results.json'];
    const captures=[];
    for(const name of manifests){
      try {
        const manifest=JSON.parse(await readFile(path.join(artifacts,name),'utf8'));
        for(const row of manifest.results??[]) {
          if(typeof row.screenshot==='string' && /^[a-z0-9-]+\.png$/.test(row.screenshot))
            captures.push({name:row.screenshot,route:row.route ?? null,viewport:row.viewport,
              kind:name==='role-results.json'?'isolated-role-component':'public-browser'});
        }
        if(name==='results.json')
          for(const row of manifest.adminNavigation??[]) if(typeof row.screenshot==='string' &&
            /^[a-z0-9-]+\.png$/.test(row.screenshot))
            captures.push({name:row.screenshot,route:'/admin',viewport:row.viewport,kind:'mocked-auth-admin'});
      } catch(error) {
        if(error.code!=='ENOENT') throw error;
      }
    }
    assert.ok(captures.length>0,'No captured screenshot manifests to compare');
    const unique=[...new Map(captures.map(row=>[row.name,row])).values()];
    const available=references && await access(references).then(()=>true).catch(()=>false);
    if(!available) {
      await writeFile(reportPath,JSON.stringify({
        status:'UNVERIFIED_REFERENCE_IMAGES_MISSING',
        captureCount:unique.length,expectedReferenceNames:unique.map(row=>row.name),
        requiredForRelease:'Same-viewport original screenshot PNGs, supplied off-repository',
        pixelDiffs:[],date:new Date().toISOString()
      },null,2)+'\n');
      console.warn('::warning::Original screenshot references are unavailable. Pixel parity is UNVERIFIED, not passing.');
      if(requireAll) throw new Error('Reference images are required by release pixel-parity gate');
    } else {
      const comparisons=[],missing=[];
      for(const record of unique) {
        const ref=path.join(references,record.name),actual=path.join(artifacts,record.name);
        // Exact basenames only, no caller-controlled relative path traversal.
        try { await access(ref); } catch(error) {if(error.code!=='ENOENT') throw error;missing.push(record.name);continue;}
        const result=await comparePngBytes(await readFile(ref),await readFile(actual));
        comparisons.push({...record,...result,threshold:maxChanged,
          result:result.exactDimensions && result.changedRatio<=maxChanged?'MATCHED_WITHIN_THRESHOLD':'MISMATCH'});
      }
      const mismatches=comparisons.filter(row=>row.result==='MISMATCH');
      const status=missing.length?'PARTIAL_REFERENCE_SET':mismatches.length?'PIXEL_PARITY_FAILED':'PIXEL_PARITY_VERIFIED';
      await writeFile(reportPath,JSON.stringify({
        status,compared:comparisons.length,expected:unique.length,missing,mismatches:mismatches.map(row=>row.name),
        colorTolerance,allowedChangedRatio:maxChanged,comparisons
      },null,2)+'\n');
      console.log(JSON.stringify({status,compared:comparisons.length,expected:unique.length,missing,mismatches:mismatches.map(row=>row.name)}));
      if(mismatches.length || requireAll && missing.length) throw new Error('Visual pixel differences exceed release gate or original screenshots are missing');
      if(missing.length) console.warn('::warning::Only a subset of original screenshots was compared. Full visual parity is UNVERIFIED.');
    }
  }
} finally {
  await browser.close();
}
