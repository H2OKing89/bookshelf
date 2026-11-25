import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const {
  cleanupConfirmation, cleanupPreviewPath, cleanupSelection, membershipStatus,
  parseCleanupMode, parseCleanupPreview, parseCleanupTags, parseMembershipSnapshot
} = loadFrontendModule('Settings/ImportLists/importListPolicyQuery.ts');

const us = 'audible:us:B011111111';
const uk = 'audible:uk:B011111111';
const candidate = { bookId: 12, recordingId: us, title: 'Owned recording', authorId: 7, authorName: 'Owned author', monitored: false };

function member(recordingId, excluded = false) {
  return { sourceAuthor: 'Owned author', sourceTitle: 'Same title', sourceBookId: recordingId,
    sourceEditionId: recordingId, authorId: 'audible:us:B055555555', bookId: recordingId,
    editionId: recordingId, excluded };
}

test('membership validation preserves independent exact source/resolved IDs and exclusions', () => {
  const snapshot = parseMembershipSnapshot({ importListId: 3, isCurrent: true,
    lastSuccessfulSync: '2026-11-24T00:00:00Z', items: [member(us), member(uk, true), { ...member(us), editionId: uk }] }, 3);
  assert.deepEqual(snapshot.items.map((entry) => entry.sourceBookId), [us, uk, us]);
  assert.deepEqual(snapshot.items.map(membershipStatus), ['Resolved identity', 'Excluded', 'Unresolved identity']);
  assert.equal(snapshot.items[0].sourceAuthorId, null);
  assert.throws(() => parseMembershipSnapshot({ ...snapshot, importListId: 4 }, 3));
  assert.throws(() => parseMembershipSnapshot({ ...snapshot, lastSuccessfulSync: 'not a timestamp' }, 3));
  assert.throws(() => parseMembershipSnapshot({ ...snapshot, items: [{ ...member(us), bookId: 12 }] }, 3));
  assert.throws(() => parseMembershipSnapshot({ ...snapshot, items: [{ ...member(us), excluded: 'false' }] }, 3));
});

test('cleanup selection is typed, safe and bound to the exact mode and tag', () => {
  assert.deepEqual(cleanupSelection('preview', '12'), { mode: 'preview', tagId: null });
  assert.equal(cleanupSelection('tag', ''), null);
  assert.equal(cleanupSelection('tag', '0'), null);
  assert.equal(cleanupSelection('tag', '1.5'), null);
  assert.equal(cleanupSelection('tag', '9999999999999999999999999'), null);
  assert.throws(() => parseCleanupMode('delete'));
  const selected = cleanupSelection('tag', '17');
  assert.equal(cleanupPreviewPath(selected), '/importlistcleanup?mode=tag&tagId=17');
  const preview = parseCleanupPreview({ ...selected, blockers: [], candidates: [candidate], confirmationToken: 'owned-token' }, selected);
  assert.deepEqual(cleanupConfirmation(preview, selected), { mode: 'tag', tagId: 17, confirmationToken: 'owned-token' });
  assert.equal(cleanupConfirmation(preview, cleanupSelection('tag', '18')), null);
  assert.equal(cleanupConfirmation(preview, cleanupSelection('unmonitor', '')), null);
  assert.throws(() => parseCleanupPreview({ ...preview, tagId: 18 }, selected));
  assert.throws(() => parseCleanupPreview({ ...preview, mode: 'unmonitor', tagId: null }, selected));
});

test('cleanup rejects invalid candidates and any token not backed by complete preview evidence', () => {
  const selection = { mode: 'unmonitor', tagId: null };
  const response = { ...selection, blockers: [], candidates: [candidate], confirmationToken: 'owned-token' };
  for (const value of [
    { ...candidate, bookId: '12' }, { ...candidate, authorId: 0 },
    { ...candidate, recordingId: 'B011111111' },
    { ...candidate, recordingId: 'local:recording:0123456789abcdef0123456789abcdef' },
    { ...candidate, monitored: 'false' }
  ]) {
    assert.throws(() => parseCleanupPreview({ ...response, candidates: [value] }, selection));
  }
  assert.throws(() => parseCleanupPreview({ ...response, blockers: ['incomplete'] }, selection));
  assert.throws(() => parseCleanupPreview({ ...response, candidates: [] }, selection));
  assert.throws(() => parseCleanupPreview({ ...response, confirmationToken: '' }, selection));
  assert.equal(cleanupConfirmation(parseCleanupPreview({ ...response, confirmationToken: null }, selection), selection), null);
  assert.deepEqual(parseCleanupTags([{ id: 17, label: 'Owned tag' }]), [{ id: 17, label: 'Owned tag' }]);
  assert.throws(() => parseCleanupTags([{ id: -1, label: 'Invalid tag' }]));
});
