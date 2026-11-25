import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const models = loadFrontendModule('Book/LibraryModels');
const { parseAssociationId, parseResourceId } = loadFrontendModule('Book/BookIdentity');

test('database identity parsing is positive and preserves distinct unassigned association sentinels', () => {
  assert.equal(parseResourceId(1, 'book'), 1);
  assert.equal(parseResourceId(2_147_483_647, 'edition'), 2_147_483_647);
  for (const value of [0, -1, 1.5, '1', 2_147_483_648, NaN, null, undefined]) {
    assert.throws(() => parseResourceId(value, 'bookFile'), TypeError);
  }
  assert.equal(parseAssociationId(0, 'author'), 0);
  assert.equal(parseAssociationId(null, 'book'), null);
  assert.equal(parseAssociationId(undefined, 'book'), undefined);
});

test('library decoding preserves ebook fields, explicit false/zero, nullable identities and legacy metadata', () => {
  const edition = { id: 5, bookId: 7, foreignEditionId: null, isEbook: true, isbn13: '9781234567890', pageCount: 0, format: 'EPUB', monitored: false, publisher: 'Owned fixture' };
  const author = { id: 3, authorName: 'Owned author', monitored: false, qualityProfileId: 0, metadataProfileId: 0, genres: ['Fiction'] };
  const raw = { id: 7, authorId: 3, title: 'Owned book', monitored: false, anyEditionOk: false, foreignEditionId: null, pageCount: 0, runtimeLengthMinutes: 0, isAdult: false, author, editions: [edition], ratings: { value: 0 } };
  const book = models.parseLibraryBook(raw);
  assert.deepEqual(book, raw);
  assert.notEqual(book, raw);
  assert.notEqual(book.editions[0], edition);
  assert.equal(book.foreignEditionId, null);
  assert.equal(book.monitored, false);
  assert.equal(book.pageCount, 0);
  assert.equal(book.editions[0].isEbook, true);
  assert.equal(book.editions[0].isbn13, edition.isbn13);
  const omitted = models.parseLibraryAuthor({ id: 3 });
  assert.equal(Object.hasOwn(omitted, 'foreignAuthorId'), false);
});

test('the library file parser supports unresolved files, Unknown quality, zero duration and default-stream false', () => {
  const file = models.parseLibraryBookFile({
    id: 42, bookId: 0, authorId: 0, path: '/owned/fixture.m4b', size: 0,
    qualityRank: null, qualityWeight: 0, qualityCutoffNotMet: false,
    quality: { quality: { id: 0, name: 'Unknown' }, revision: { real: 0, version: 0, isRepack: false } },
    mediaInfo: { audioChannels: 0, durationSeconds: 0, audioStreams: [{ index: 0, audioChannels: 0, durationSeconds: 0, isDefault: false }] }
  });
  assert.equal(file.bookId, 0);
  assert.equal(file.authorId, 0);
  assert.equal(file.size, 0);
  assert.equal(file.qualityRank, null);
  assert.equal(file.quality.quality.id, 0);
  assert.equal(file.quality.revision.isRepack, false);
  assert.equal(file.mediaInfo.durationSeconds, 0);
  assert.equal(file.mediaInfo.audioStreams[0].isDefault, false);
  assert.equal(file.mediaInfo.audioStreams[0].index, 0);
  assert.equal(models.parseLibraryBookFile({ id: 1, mediaInfo: null }).mediaInfo, null);
});

test('invalid library resource shape, duplicate IDs and wrong nested/requested identities are rejected', () => {
  const failures = [
    () => models.parseLibraryAuthor({ id: 0 }),
    () => models.parseLibraryBook({ id: 1, authorId: 2, author: { id: 3 } }),
    () => models.parseLibraryBook({ id: 1, editions: [{ id: 5, bookId: 2 }] }),
    () => models.parseLibraryBook({ id: 1, monitored: 'false' }),
    () => models.parseLibraryEdition({ id: 1, isEbook: 'true' }),
    () => models.parseLibraryBookFile({ id: 1, bookId: -1 }),
    () => models.parseLibraryBookFile({ id: 1, size: -1 }),
    () => models.parseLibraryBookFile({ id: 1, mediaInfo: { audioStreams: { index: 0 } } }),
    () => models.parseLibraryCollection([{ id: 1 }, { id: 1 }], models.parseLibraryAuthor),
    () => models.libraryResponseParser(models.parseLibraryAuthor)({ id: 5 }, 6)
  ];
  for (const invalid of failures) {
    assert.throws(invalid, TypeError);
  }
});

test('a malformed legacy fetch is reported without replacing library data or leaving a spinner active', () => {
  const dispatched = [];
  let deliver;
  let fail;
  const request = {
    done(callback) { deliver = callback; },
    fail(callback) { fail = callback; }
  };
  const { default: createFetchHandler } = loadFrontendModule('Store/Actions/Creators/createFetchHandler', {
    'Utilities/createAjaxRequest': () => ({ request, abortRequest: () => {} }),
    'redux-batched-actions': { batchActions: (actions) => actions },
    '../baseActions': {
      set: (payload) => ({ type: 'set', payload }),
      update: (payload) => ({ type: 'update', payload }),
      updateItem: (payload) => ({ type: 'updateItem', payload })
    }
  });
  const handler = createFetchHandler('authors', '/author', models.libraryResponseParser(models.parseLibraryAuthor));
  handler(() => ({}), {}, (action) => dispatched.push(action));
  deliver([{ id: 0 }]);
  assert.equal(dispatched.length, 2);
  assert.equal(dispatched[1].type, 'set');
  assert.equal(dispatched[1].payload.isFetching, false);
  assert.equal(dispatched[1].payload.isPopulated, false);
  assert.equal(dispatched[1].payload.error.kind, 'invalid-response');

  dispatched.length = 0;
  handler(() => ({}), { id: 3 }, (action) => dispatched.push(action));
  deliver({ id: 3, monitored: false });
  assert.deepEqual(dispatched[1][0], { type: 'updateItem', payload: { section: 'authors', id: 3, monitored: false } });
  assert.equal(dispatched[1][1].payload.isPopulated, true);
  assert.equal(typeof fail, 'function');
});
