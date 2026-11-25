import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const [filterTypes, { default: filterTypePredicates }, sortDirections] = loadFrontendModule([
  'Helpers/Props/filterTypes', 'Helpers/Props/filterTypePredicates', 'Helpers/Props/sortDirections'
]);
const boundaries = { 'Helpers/Props': { filterTypes, filterTypePredicates, sortDirections } };
const [
  { default: createBooks },
  { default: createBookAuthor },
  { default: createBookQualityProfile },
  { default: createCollection },
  { default: filterCollection },
  { default: sortCollection }
] = loadFrontendModule([
  'Store/Selectors/createBooksClientSideCollectionSelector',
  'Store/Selectors/createBookAuthorSelector',
  'Store/Selectors/createBookQualityProfileSelector',
  'Store/Selectors/createClientSideCollectionSelector',
  'Utilities/Array/filterCollection',
  'Utilities/Array/sortCollection'
], boundaries);

function frozenState() {
  const authors = [
    Object.freeze({ id: 10, sortName: 'Zulu', authorName: 'Zulu', qualityProfileId: 1, path: '/owned/z' }),
    Object.freeze({ id: 11, sortName: 'Alpha', authorName: 'Alpha', qualityProfileId: 2, path: '/owned/a' })
  ];
  const books = [
    Object.freeze({ id: 21, authorId: 10, title: 'Recording A', foreignEditionId: 'US-1', monitored: false }),
    Object.freeze({ id: 22, authorId: 11, title: 'Recording B', foreignEditionId: 'UK-1', monitored: true })
  ];
  return Object.freeze({
    books: Object.freeze({ items: Object.freeze(books), itemMap: Object.freeze({ 21: 0, 22: 1 }) }),
    // Deliberately stale map: an id must never borrow the other owner's profile.
    authors: Object.freeze({ items: Object.freeze(authors), itemMap: Object.freeze({ 10: 1, 11: 0 }) }),
    customFilters: Object.freeze({ items: Object.freeze([]) }),
    settings: Object.freeze({ qualityProfiles: Object.freeze({ items: Object.freeze([
      Object.freeze({ id: 1, name: 'Original' }), Object.freeze({ id: 2, name: 'Alternate' })
    ]) }) }),
    bookIndex: Object.freeze({
      sortKey: 'authorName', sortDirection: 'ascending', selectedFilterKey: 'all',
      sortPredicates: Object.freeze({ authorName: (book) => book.author?.sortName }),
      filterPredicates: Object.freeze({ author: (book, name) => book.author?.authorName === name }),
      filters: Object.freeze([{ key: 'alpha', filters: [{ key: 'author', value: 'Alpha' }] }])
    })
  });
}

test('book collection joins frozen Redux resources by actual author IDs, never by a stale map or mutation', () => {
  const state = frozenState();
  const select = createBooks('bookIndex');
  const result = select(state);
  assert.deepEqual(result.items.map((book) => book.id), [22, 21]);
  assert.equal(result.items[0].author, state.authors.items[1]);
  assert.equal(result.items[1].author, state.authors.items[0]);
  assert.notEqual(result.items[0], state.books.items[1]);
  assert.equal(result.items[0].foreignEditionId, 'UK-1');
  assert.equal(result.items[1].foreignEditionId, 'US-1');
  assert.equal(Object.hasOwn(state.books.items[0], 'author'), false);
  assert.equal(Object.hasOwn(state.books.items[1], 'author'), false);
  assert.equal(select(state), result);

  const filtered = select({ ...state, bookIndex: { ...state.bookIndex, selectedFilterKey: 'alpha' } });
  assert.deepEqual(filtered.items.map((book) => book.id), [22]);
  assert.equal(filtered.totalItems, 2);
  assert.equal(filtered.selectedFilterKey, 'alpha');

  const changed = { ...state, authors: {
    ...state.authors,
    items: [Object.freeze({ ...state.authors.items[0], sortName: 'Aardvark', qualityProfileId: 2 }), state.authors.items[1]]
  } };
  const next = select(changed);
  assert.deepEqual(next.items.map((book) => book.id), [21, 22]);
  assert.equal(next.items[0].author.qualityProfileId, 2);
  assert.equal(result.items[1].author.qualityProfileId, 1);
  assert.equal(state.books.items[0].author, undefined);
  assert.equal(changed.books, state.books);
});

test('author and profile selectors follow the actual association through stale maps, missing books and profile updates', () => {
  const state = frozenState();
  const selectAuthor = createBookAuthor();
  const selectProfile = createBookQualityProfile();
  const props = { bookId: 21 };
  assert.equal(selectAuthor(state, props), state.authors.items[0]);
  assert.equal(selectProfile(state, props).id, 1);
  assert.equal(selectAuthor(state, { bookId: 999 }), undefined);
  assert.deepEqual(selectProfile(state, { bookId: 999 }), {});

  const changed = { ...state, authors: {
    ...state.authors,
    items: [Object.freeze({ ...state.authors.items[0], qualityProfileId: 2 }), state.authors.items[1]]
  } };
  assert.equal(selectProfile(changed, props).id, 2);
  assert.equal(selectProfile(state, props).id, 1);
  const authorRemoved = { ...changed, authors: { items: [changed.authors.items[1]], itemMap: { 10: 0, 11: 0 } } };
  assert.equal(selectAuthor(authorRemoved, props), undefined);
  assert.deepEqual(selectProfile(authorRemoved, props), {});
});

test('typed collection filtering and ordering preserve falsy values, custom predicates, retired predicates and stable tie order', () => {
  const items = Object.freeze([
    Object.freeze({ id: 1, count: 0, monitored: false, values: ['one'], rank: 2 }),
    Object.freeze({ id: 2, count: 1, monitored: true, values: ['two'], rank: 1 }),
    Object.freeze({ id: 3, count: 0, monitored: false, values: ['one', 'two'], rank: 1 })
  ]);
  const select = (rule) => filterCollection(items, {
    selectedFilterKey: 'custom', filters: [{ key: 'custom', filters: [rule] }]
  });
  assert.equal(filterCollection(items, {}), items);
  assert.deepEqual(select({ key: 'count', value: 0 }).map((item) => item.id), [1, 3]);
  assert.deepEqual(select({ key: 'monitored', value: false }).map((item) => item.id), [1, 3]);
  assert.deepEqual(select({ key: 'values', type: 'notContains', value: ['one', 'two'] }), []);
  assert.deepEqual(select({ key: 'values', type: 'contains', value: ['none', 'two'] }).map((item) => item.id), [2, 3]);
  assert.deepEqual(select({ key: 'count', type: 'retired', value: 0 }), []);
  assert.deepEqual(filterCollection(items, {
    selectedFilterKey: 'custom', filters: [{ key: 'custom', filters: [{ key: 'count', value: 0 }] }],
    filterPredicates: { count: null }
  }), []);
  assert.deepEqual(sortCollection(items, {
    sortKey: 'rank', sortDirection: 'ascending', secondarySortKey: 'id', secondarySortDirection: 'descending'
  }).map((item) => item.id), [3, 2, 1]);
  assert.deepEqual(items.map((item) => item.id), [1, 2, 3]);
});

test('typed provider collection selector keeps provider metadata and resource identities', () => {
  const items = [{ id: 1, name: 'B', enable: false }, { id: 2, name: 'A', enable: true }];
  const section = {
    items, isFetching: false, isPopulated: true, isSaving: false, sortKey: 'name', sortDirection: 'ascending'
  };
  const state = { settings: { downloadClients: section }, customFilters: { items: [] } };
  const select = createCollection('settings.downloadClients');
  const result = select(state);
  assert.equal(result.isPopulated, true);
  assert.equal(result.isFetching, false);
  assert.equal(result.items[0], items[1]);
  assert.equal(result.items[1], items[0]);
  assert.equal(result.totalItems, 2);
  assert.equal(select(state), result);
  assert.deepEqual(section.items.map((item) => item.id), [1, 2]);
});
