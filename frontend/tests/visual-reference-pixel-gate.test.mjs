import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const load = path => readFileSync(new URL('../../' + path,import.meta.url),'utf8');
const pixelTool=load('frontend/scripts/compare-reference-screenshots.mjs');
const workflow=load('.github/workflows/browser-ui-visual-audit.yml');

test('Pixel parity requires actual original PNG references; missing baseline is UNVERIFIED, not success',()=>{
  assert.match(pixelTool,/LEFTERIA_REFERENCE_SCREENSHOT_DIR/);
  assert.match(pixelTool,/LEFTERIA_REQUIRE_PIXEL_PARITY/);
  assert.match(pixelTool,/UNVERIFIED_REFERENCE_IMAGES_MISSING/);
  assert.match(pixelTool,/if\(requireAll\) throw new Error\('Reference images are required/);
  assert.match(pixelTool,/if\(mismatches\.length \|\| requireAll && missing\.length\) throw/);
  assert.match(pixelTool,/PARTIAL_REFERENCE_SET/);
  assert.match(pixelTool,/PIXEL_PARITY_FAILED/);
  assert.match(pixelTool,/PIXEL_PARITY_VERIFIED/);
  assert.doesNotMatch(pixelTool,/http:\/\/lefteriafc\.cy|https:\/\/lefteriafc\.cy/);
});

test('Chromium performs real decoded full-page pixel sampling, not compression/OCR/proxy metrics',()=>{
  assert.match(pixelTool,/chromium\.launch/);
  assert.match(pixelTool,/new Image\(\)/);
  assert.match(pixelTool,/data:image\/png;base64/);
  assert.match(pixelTool,/getImageData/);
  assert.match(pixelTool,/for\(let top=0;top<height;top\+=chunkHeight\)/);
  assert.match(pixelTool,/a\.naturalWidth===b\.naturalWidth && a\.naturalHeight===b\.naturalHeight/);
  assert.match(pixelTool,/changed\/total/);
  assert.match(pixelTool,/meanAbsoluteRgb/);
  assert.match(pixelTool,/selfTest/);
  assert.match(pixelTool,/assert\.equal\(mismatch\.changed,1\)/);
  assert.match(pixelTool,/assert\.equal\(mismatch\.changedRatio,1\/24\)/);
});

test('All CI captures are inventoried, including authenticated sidebar mocks and role-component layout evidence',()=>{
  assert.match(pixelTool,/\['results\.json','role-results\.json','authenticated-role-results\.json'\]/);
  assert.match(pixelTool,/secondaryScreenshot/);
  assert.match(pixelTool,/mocked-mobile-auth/);
  assert.match(pixelTool,/adminNavigation/);
  assert.match(pixelTool,/mocked-auth-admin/);
  assert.match(pixelTool,/isolated-role-component/);
  assert.match(pixelTool,/visual-pixel-comparison\.json/);
  assert.match(workflow,/compare-reference-screenshots\.mjs --self-test/);
  assert.match(workflow,/node scripts\/compare-reference-screenshots\.mjs\n/);
  assert.match(workflow,/actions\/upload-artifact/);
});
