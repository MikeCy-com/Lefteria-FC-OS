import test from 'node:test';
import assert from 'node:assert/strict';
import { firstTeamHeroBanner } from '../src/lib/public/first-team-hero.ts';

const team = (id, level, banner_url, organization_id = 'lefteria_fc') =>
  ({ id, level, banner_url, organization_id, name: id, description: null });

test('First Team hero ignores earlier unrelated Club team banners', () => {
  assert.equal(firstTeamHeroBanner([
    team('women', 'Women', '/women-banner.jpg'),
    team('selected', 'Selected', '/selected-banner.jpg'),
    team('first', 'First Team', '/verified-first-team.jpg')
  ]), '/verified-first-team.jpg');
});

test('Academy never supplies the Club First Team hero banner', () => {
  assert.equal(firstTeamHeroBanner([
    team('academy', 'First Team', '/academy-banner.jpg', 'lefteria_fc_academy'),
    team('women', 'Women', '/women-banner.jpg'),
    team('first', 'Πρώτη Ομάδα', '/real-first-team.jpg')
  ]), '/real-first-team.jpg');
});

test('unverified and unavailable First Team banners use safe editorial fallback', () => {
  assert.equal(firstTeamHeroBanner([team('women', 'Women', '/women-banner.jpg')]), null);
  assert.equal(firstTeamHeroBanner([
    team('women', 'Women', '/women-banner.jpg'),
    team('first', 'First Team', null)
  ]), null);
  assert.equal(firstTeamHeroBanner([]), null);
  assert.equal(firstTeamHeroBanner(null), null);
});
