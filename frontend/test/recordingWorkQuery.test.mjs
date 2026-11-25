import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const recording = { editionId: 7, bookId: 5, foreignEditionId: 'audible:uk:B011111111', asin: 'B011111111',
  audibleRegion: 'uk', title: 'Owned recording', author: 'Owned author', bookSlug: 'owned', monitored: false, fileCount: 0, sizeOnDisk: 0 };
const snapshot = { recording, revision: 'owned-review', memberships: [{
  work: { id: 11, title: 'Owned work', author: 'Owned author', series: '', position: '0' },
  confirmed: false, hasConfirmedFiles: false, recordings: []
}], unmatchedMembershipCount: 0, coveredWorkCount: 0, relatedRecordingCount: 0, relatedFileCount: 0, relatedSizeOnDisk: 0 };

test('work coverage read and save stay bound to the requested book or edition and preserve zero/false', async (t) => {
  const previous = globalThis.window;
  globalThis.window = { Readarr: { apiRoot: '/owned/api/v1' } };
  t.after(() => {
    if (previous) globalThis.window = previous;
    else delete globalThis.window;
  });
  let body;
  let response;
  const api = loadFrontendModule('Book/WorkCoverage/recordingWorkQuery', {
    'Utilities/Fetch/fetchJson': async (options) => {
      body = options.body;
      return options.parse(response);
    }
  });
  const context = { signal: new AbortController().signal };

  response = [recording];
  assert.equal((await api.recordingsForBook(5).queryFn(context))[0].monitored, false);
  await assert.rejects(api.recordingsForBook(6).queryFn(context), /different book/);
  assert.throws(() => api.recordingsForBook(0), /Invalid book ID/);
  response = snapshot;
  const read = await api.recordingCoverage(7).queryFn(context);
  assert.equal(read.recording.fileCount, 0);
  assert.equal(read.memberships[0].confirmed, false);
  await assert.rejects(api.recordingCoverage(9).queryFn(context), /different recording/);

  await api.saveWorkCoverage(7, 'owned-review', [{ workId: 11, confirmed: false }]);
  assert.deepEqual(body, { revision: 'owned-review', memberships: [{ workId: 11, confirmed: false }] });
  await assert.rejects(api.saveWorkCoverage(9, 'owned-review', [{ workId: 11, confirmed: false }]), /different recording/);
});
