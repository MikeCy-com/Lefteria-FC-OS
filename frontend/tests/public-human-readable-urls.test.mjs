import test from 'node:test';
import assert from 'node:assert/strict';
import { academyGroupLink } from '../src/lib/public/navigation.ts';
import {
  publicSlug, personPublicLink, fixturePublicLink, publicCanonicalHref
} from '../src/lib/public/public-urls.ts';

test('Greek public identities have ASCII readable names with stable opaque identities', () => {
  assert.equal(publicSlug('Ανδρέας Παπαδόπουλος'), 'andreas-papadopoylos');
  assert.equal(publicSlug('U12 Λεμεσού'), 'u12-lemesoy');
  assert.equal(publicSlug('Ακαδημία Κ-12'), 'akadimia-k-12');
  assert.equal(personPublicLink('player',{id:'130d36bf-d0ae-4b6a-98f0-b29cbba629c5',name:'Ανδρέας Πα.'}),
    '/player/andreas-pa--130d36bf-d0ae-4b6a-98f0-b29cbba629c5');
  assert.equal(academyGroupLink({id:'4852141a-3340-406c-960a-da2b6a6dcdf8',name:'U12 Λ.'}),
    '/academy/u12-l--4852141a-3340-406c-960a-da2b6a6dcdf8');
  assert.equal(fixturePublicLink({id:'abc--ID',home_team:'Λευτεριά FC',away_team:'ΑΕΛ Λεμεσού'}),
    '/match/leyteria-fc-vs-ael-lemesoy--abc--ID');
  assert.notEqual(personPublicLink('staff',{id:'first',name:'Coach'}),personPublicLink('staff',{id:'second',name:'Coach'}));
});

test('unsafe characters do not reach route name or canonical query; unknown display name retains stable ID', () => {
  assert.equal(publicSlug(' <script>alert(1)</script> '),'script-alert-1-script');
  assert.equal(personPublicLink('player',{id:'exact%id/1',name:'  '}),'/player/exact%25id%2F1');
  assert.equal(fixturePublicLink({id:'raw',home_team:'',away_team:''}),'/match/raw');
  assert.equal(publicCanonicalHref('/player/andreas-pa--id?team_id=private&token=secret'),
    'https://lefteriafc.cy/player/andreas-pa--id');
  assert.equal(publicCanonicalHref('//evil.test/'), 'https://lefteriafc.cy/');
});
