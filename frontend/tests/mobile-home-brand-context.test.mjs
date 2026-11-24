import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const file=path=>readFileSync(new URL('../src/'+path,import.meta.url),'utf8');
const route=file('routes/app/[...path]/+page.svelte');
const home=file('lib/mobile/components/MobileRoleHome.svelte');
const shell=file('lib/mobile/components/MobileShell.svelte');
const theme=file('lib/mobile/components/mobile-theme.css');

test('member header, document title and role-home banner share verified organization branding',()=>{
  assert.match(route,/const mobileClubName=\$derived\(publicClubBrand\?\.name/);
  assert.match(route,/brandOwner==='lefteria_fc_academy'\?'ΛΕΥΤΕΡΙΑ FC ACADEMY'/);
  assert.match(route,/brandOwner==='lefteria_fc'\?'ΛΕΥΤΕΡΙΑ FC':'ΕΠΙΛΟΓΗ ΟΡΓΑΝΙΣΜΟΥ'/);
  assert.equal((route.match(/clubName=\{mobileClubName\}/g)||[]).length,2,
    'both MobileShell and MobileRoleHome must receive the same verified name');
  assert.match(route,/<title>\{TAB_NAMES\[activeTab\]\} \| \{mobileClubName\}<\/title>/);
  assert.match(home,/clubName\?:string/);
  assert.match(home,/\{clubName\} · \{ROLE_NAMES\[role\]\}/);
  assert.doesNotMatch(home,/>ΛΕΥΤΕΡΙΑ FC · \{ROLE_NAMES\[role\]\}</);
  assert.match(shell,/data-testid="mobile-club-brand-name"/);
});

test('Academy and management hero images cannot receive a fixed first-team watermark',()=>{
  assert.doesNotMatch(theme,/background:\s*url\(['"]\/mobile\/club-logo\.png/);
  assert.match(theme,/\.parent-greeting::after,[\s\S]*?\.role-home \.hero::after \{\s*content: none;/);
  assert.match(home,/publicImages\.hero/,'retain the original photographic hero until approved tenant-specific media');
  assert.match(route,/readPublicBrand\(raw,owner\)/,'brand stays public, validated and owner-scoped');
  assert.match(route,/credentials:'omit'/,'public branding must not forward session credentials');
});
