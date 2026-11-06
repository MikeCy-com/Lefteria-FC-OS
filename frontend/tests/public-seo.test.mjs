import test from 'node:test';
import assert from 'node:assert/strict';
import {
  CANONICAL_PUBLIC_ORIGIN, buildPublicSitemap, isIndexablePublicOrigin, robotsFor
} from '../src/lib/public/seo.ts';

test('production origin only: preview, localhost, HTTP, alternate host and port do not index', () => {
  assert.equal(CANONICAL_PUBLIC_ORIGIN, 'https://lefteriafc.cy');
  for (const address of ['https://lefteriafc.cy/', 'https://www.lefteriafc.cy/path']) {
    assert.equal(isIndexablePublicOrigin(new URL(address)), true, address);
    assert.match(robotsFor(new URL(address)), /Sitemap: https:\/\/lefteriafc\.cy\/sitemap\.xml/);
    assert.match(robotsFor(new URL(address)), /Disallow: \/admin/);
    assert.match(robotsFor(new URL(address)), /Disallow: \/app/);
  }
  for (const address of ['https://preview.lefteriafc.cy/', 'https://lefteriafc.cy:8443/',
    'http://lefteriafc.cy/', 'http://localhost:3000/', 'https://lefteriafc.cy.evil.invalid/',
    'https://user:password@lefteriafc.cy/']) {
    assert.equal(isIndexablePublicOrigin(new URL(address)), false, address);
    assert.equal(robotsFor(new URL(address)), 'User-agent: *\nDisallow: /\n', address);
  }
});

test('published sitemap includes actual public routes, global news and eligible adults but never Academy minors', () => {
  const fc = {
    organization_id: 'lefteria_fc',
    players: [
      { id: 'first-team-adult', organization_id: 'lefteria_fc', team_type: 'First Team', age: 24, name: 'Senior A' },
      { id: 'minor-first-team', organization_id: 'lefteria_fc', team_type: 'First Team', age: 17, name: 'Minor B' },
      { id: 'unknown-age', organization_id: 'lefteria_fc', team_type: 'First Team', age: null },
      { id: 'youth-record', organization_id: 'lefteria_fc', team_type: 'Academy', age: 23 }
    ],
    staff: [{ id: 'public-coach', organization_id: 'lefteria_fc' }],
    events: [{ id: 'shared-event', title: 'Club & Academy Day', organization_id: 'lefteria_fc' }],
    partners: [{ slug: 'partner-one', organization_id: 'lefteria_fc' }],
    sponsors: [{ id: 'sponsor-one', name: 'Sponsor', organization_id: 'lefteria_fc' }],
    archived_seasons: [{ id: 'old-season', season_name: '2024/2025', organization_id: 'lefteria_fc' }]
  };
  const academy = {
    organization_id: 'lefteria_fc_academy',
    players: [{ id: 'academy-minor', organization_id: 'lefteria_fc_academy', team_type: 'Academy', age: 13 }],
    staff: [{ id: 'academy-private-coach', organization_id: 'lefteria_fc_academy' }],
    events: [{ id: 'shared-event', title: 'Club & Academy Day', organization_id: 'lefteria_fc_academy' }],
    partners: [], sponsors: [], archived_seasons: []
  };
  const articles = [
    { id: 'published-news', title: 'Ποδόσφαιρο & Ακαδημία', created_at: '2026-11-01T08:00:00Z' },
    { id: '<unsafe&', title: 'Other <script>alert(1)</script>', created_at: null }
  ];
  const sites = [
    { organization_id: 'lefteria_fc', fixtures: [
      { id: 'first-team-game', organization_id: 'lefteria_fc' },
      { id: 'ambiguous-id', organization_id: 'lefteria_fc' }
    ] },
    { organization_id: 'lefteria_fc_academy', fixtures: [
      { id: 'academy-game', organization_id: 'lefteria_fc_academy' },
      { id: 'ambiguous-id', organization_id: 'lefteria_fc_academy' }
    ] }
  ];
  const groups = [
    { id: 'u12', name: 'U12 Λεμεσού', organization_id: 'lefteria_fc_academy' },
    { id: 'foreign-group', name: 'Not Academy', organization_id: 'lefteria_fc' }
  ];

  const xml = buildPublicSitemap({ articles, groups, sites, sections: [fc, academy] });
  for (const item of ['/', '/academy', '/academy/philosophy', '/news', '/team', '/events',
    '/player/first-team-adult', '/staff/public-coach', '/match/first-team-game',
    '/match/academy-game', '/events/', '/partners/partner-one', '/sponsors/', '/seasons/']) {
    assert.ok(xml.includes(CANONICAL_PUBLIC_ORIGIN + item), item);
  }
  assert.match(xml, /\/academy\/u12--u12/);
  assert.match(xml, /\/news\/[^<]+--published-news/);
  assert.match(xml, /<lastmod>2026-11-01<\/lastmod>/);
  assert.match(xml, /\/events\/[^<]+\?scope=academy/);
  assert.doesNotMatch(xml, /minor-first-team|academy-minor|unknown-age|youth-record|foreign-group/);
  assert.doesNotMatch(xml, /academy-private-coach|ambiguous-id|<script>|medical_notes|phone_number/);
  assert.doesNotMatch(xml, /\/app\/|\/admin\/|\/checkout\/|\/cart\/|\/register\//);
  assert.equal((xml.match(/<loc>https:\/\/lefteriafc\.cy\//g) ?? []).length,
    new Set([...xml.matchAll(/<loc>(.*?)<\/loc>/g)].map(match => match[1])).size,
    'canonical locations are unique');
  assert.ok(xml.startsWith('<?xml version="1.0" encoding="UTF-8"?>\n'));
  assert.match(xml, /<urlset xmlns="http:\/\/www\.sitemaps\.org\/schemas\/sitemap\/0\.9">/);
});

test('XML serialization escapes query strings and rejects source IDs that alter URL routing', () => {
  const empty = { organization_id: 'lefteria_fc', players: [], staff: [], events: [], partners: [], sponsors: [], archived_seasons: [] };
  const xml = buildPublicSitemap({
    articles: [], groups: [], sites: [{ organization_id: 'lefteria_fc', fixtures: [
      { id: '..', organization_id: 'lefteria_fc' },
      { id: 'abc/def', organization_id: 'lefteria_fc' }
    ] }], sections: [{ ...empty, events: [
      { id: 'event-1', title: 'A & B', organization_id: 'lefteria_fc' }
    ] }]
  });
  assert.match(xml, /event-1\?scope=first_team/);
  assert.doesNotMatch(xml, /abc%2Fdef|\.\.\/match/);
  assert.doesNotMatch(xml, /<loc>[^<]*&(?!amp;)/);
});
