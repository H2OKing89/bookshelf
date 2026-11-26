import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { formatToken, insertNamingToken, namingDrafts, namingExamples } = loadFrontendModule('Settings/MediaManagement/Naming/namingSettingsState.ts');

test('naming drafts retain zero, false, null and server validation', () => {
  const errors = [{ message: 'Invalid format' }];
  const value = namingDrafts({
    renameBooks: { value: false },
    replaceIllegalCharacters: { value: false },
    colonReplacementFormat: { value: 0, pending: true },
    standardBookFormat: { value: '', errors },
    authorFolderFormat: { value: null }
  });
  assert.equal(value.renameBooks.value, false);
  assert.equal(value.replaceIllegalCharacters.value, false);
  assert.equal(value.colonReplacementFormat.value, 0);
  assert.equal(value.colonReplacementFormat.pending, true);
  assert.equal(value.standardBookFormat.value, '');
  assert.equal(value.standardBookFormat.errors, errors);
  assert.equal(value.authorFolderFormat.value, null);
  assert.deepEqual(namingExamples({ singleBookExample: 'Book', multiPartBookExample: {}, authorFolderExample: null }), {
    singleBookExample: 'Book', multiPartBookExample: undefined, authorFolderExample: undefined
  });
});

test('naming token casing preserves the chosen separator', () => {
  assert.equal(formatToken('{Author Name}', '.', 'lower'), '{author.name}');
  assert.equal(formatToken('{Book Title}', '_', 'upper'), '{BOOK_TITLE}');
  assert.equal(formatToken('{Book Title}', '-', 'title'), '{Book-Title}');
});

test('naming tokens insert at zero or replace a selected range and keep the insertion cursor', () => {
  assert.deepEqual(insertNamingToken('ABCD', 'token', 0, 0, false), { value: 'tokenABCD', cursor: 5 });
  const insertion = insertNamingToken('ABCD', 'token', 1, 3, false);
  assert.deepEqual(insertion, { value: 'AtokenD', cursor: 6 });
  assert.deepEqual(insertNamingToken(insertion.value, 'X', insertion.cursor, insertion.cursor, false), { value: 'AtokenXD', cursor: 7 });
  assert.deepEqual(insertNamingToken(null, 'X', null, null, false), { value: 'X', cursor: 1 });
  assert.deepEqual(insertNamingToken('ABCD', 'X', 2, 2, true), { value: 'X', cursor: 1 });
});

test('naming example debounce is cleared when navigating away', (context) => {
  context.mock.timers.enable({ apis: ['setTimeout'] });
  const { NamingConnector } = loadFrontendModule('Settings/MediaManagement/Naming/NamingConnector.tsx', {
    'react-redux': { connect: () => (Component) => Component },
    'Store/Actions/baseActions': {},
    'Store/Actions/settingsActions': {},
    './Naming': () => null
  });
  const calls = [];
  const component = new NamingConnector({
    setNamingSettingsValue: (change) => calls.push(change),
    fetchNamingExamples: () => calls.push('examples'),
    clearPendingChanges: (change) => calls.push(change)
  });
  component.onInputChange({ name: 'colonReplacementFormat', value: 0 });
  component.onInputChange({ name: 'renameBooks', value: false });
  context.mock.timers.tick(1000);
  assert.deepEqual(calls, [
    { name: 'colonReplacementFormat', value: 0 },
    { name: 'renameBooks', value: false },
    'examples'
  ]);
  component.onInputChange({ name: 'authorFolderFormat', value: '' });
  component.componentWillUnmount();
  context.mock.timers.tick(1000);
  assert.deepEqual(calls.slice(3), [
    { name: 'authorFolderFormat', value: '' },
    { section: 'settings.naming' }
  ]);
});
