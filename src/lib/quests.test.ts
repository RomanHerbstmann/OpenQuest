import assert from 'node:assert/strict';
import { describe, it } from 'node:test';
import { errorText, type MyClaimDto, type QuestDto } from './api';
import { claimPoints, claimState, mergeQuestTrees, questToTree } from './quests';
import { mergeNearby } from './useNearbyQuests';

const quest = (overrides: Partial<QuestDto> = {}, attributes: Record<string, unknown> = {}): QuestDto => ({
  id: 'q1',
  title: 'Fotografiere diesen Baum',
  description: null,
  taskType: 'photo',
  taskConfig: {},
  rewardPoints: 25,
  freeSlots: 3,
  geofenceRadiusM: 30,
  endsAt: null,
  distanceMeters: 12,
  asset: { id: 'a1', assetType: 'tree', externalId: 'x', lat: 51.9642, lon: 7.6275, attributes: { genus: 'Quercus', genus_raw: 'Quercus', street_key: '04905', quality_flags: [], ...attributes } },
  ...overrides,
});

const claim = (overrides: Partial<MyClaimDto> = {}): MyClaimDto => ({
  id: 'c1',
  status: 'active',
  claimedAt: '2026-09-26T10:00:00Z',
  expiresAt: new Date(Date.now() + 600_000).toISOString(),
  quest: quest(),
  submission: null,
  ...overrides,
});

describe('questToTree', () => {
  it('maps a photo quest with German genus name, street and reward', () => {
    const tree = questToTree(quest())!;
    assert.equal(tree.id, 'quest-q1');
    assert.equal(tree.species, 'Eiche');
    assert.equal(tree.speciesLatin, 'Quercus');
    assert.equal(tree.area, 'Neubrückenstraße');
    assert.equal(tree.xpReward, 25);
    assert.equal(tree.lexiconSpecies, 'Stieleiche');
    assert.deepEqual([tree.quest?.kind, tree.quest?.genus, tree.quest?.claim], ['photo', 'Quercus', null]);
  });

  it('treats a placeholder genus as unknown for verify quests', () => {
    const tree = questToTree(quest({ taskType: 'verify_attribute', taskConfig: { attribute: 'genus' } }, { genus: null, genus_raw: 'Baumgruppe', quality_flags: ['placeholder_genus'] }))!;
    assert.equal(tree.quest?.kind, 'verify');
    assert.equal(tree.quest?.attribute, 'genus');
    assert.equal(tree.quest?.genus, null);
    assert.equal(tree.species, 'Unbekannte Baumart');
    assert.equal(tree.inventory, undefined);
  });

  it('skips task types without a flow in the app', () => {
    assert.equal(questToTree(quest({ taskType: 'measure' })), null);
  });
});

describe('mergeQuestTrees', () => {
  it('keeps active claims on the map and drops duplicates from nearby', () => {
    const trees = mergeQuestTrees([quest(), quest({ id: 'q2' })], [claim()]);
    assert.deepEqual(trees.map((tree) => [tree.quest?.id, tree.quest?.claim?.id ?? null]), [['q1', 'c1'], ['q2', null]]);
  });

  it('ignores expired and submitted claims', () => {
    const trees = mergeQuestTrees([], [claim({ expiresAt: '2020-01-01T00:00:00Z' }), claim({ id: 'c2', status: 'submitted' })]);
    assert.equal(trees.length, 0);
  });
});

describe('claims', () => {
  it('counts approved XP as confirmed and pending XP separately', () => {
    const submission = (status: 'pending' | 'approved' | 'rejected') => ({ id: 's', status, rejectionReason: null, submittedAt: '2026-09-26T10:05:00Z' });
    const list = [claim({ status: 'submitted', submission: submission('approved') }), claim({ status: 'submitted', submission: submission('pending'), quest: quest({ rewardPoints: 40 }) }), claim({ status: 'submitted', submission: submission('rejected') })];
    assert.deepEqual(claimPoints(list), { confirmed: 25, pending: 40 });
    assert.deepEqual(list.map(claimState), ['approved', 'pending', 'rejected']);
    assert.equal(claimState(claim({ expiresAt: '2020-01-01T00:00:00Z' })), 'expired');
  });

  it('merges nearby answers by id, nearest first', () => {
    const merged = mergeNearby([quest({ id: 'a', distanceMeters: 50 })], [quest({ id: 'b', distanceMeters: 5 }), quest({ id: 'a', distanceMeters: 50 })]);
    assert.deepEqual(merged.map((item) => item.id), ['b', 'a']);
  });
});

describe('errorText', () => {
  it('explains the geofence with distances', () => {
    const { code, message } = errorText(422, { error: 'outside_geofence', details: { distanceMeters: 84.2, allowedMeters: 30 } });
    assert.equal(code, 'outside_geofence');
    assert.match(message, /84 m .* 30 m/);
  });

  it('maps known codes, validation problems and bare statuses', () => {
    assert.equal(errorText(409, { error: 'no_free_slots' }).message, 'Alle Plätze dieser Quest sind vergeben.');
    assert.match(errorText(400, { error: 'validation_failed', details: { password: ['8-128 characters required.'] } }).message, /Mindestens 8 Zeichen/);
    assert.equal(errorText(429, null).code, 'rate_limited');
    assert.equal(errorText(500, null).message, 'Der Server hat unerwartet geantwortet. Bitte versuche es noch einmal.');
  });
});
