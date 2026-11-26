import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const { projectAuthorHistory } = loadFrontendModule('Author/History/authorHistoryState.ts');
const { editorFiles, qualityChoices, selectedFileIds, editorColumns } = loadFrontendModule('BookFile/Editor/bookFileEditorState.ts');

test('author history preserves unknown event data, zero scores and missing metadata without changing the source', () => {
  const data = { reason: 'Synthetic', count: 0, flag: false, value: null };
  const item = { id: 1, authorId: 2, bookId: 0, eventType: 99, data, customFormatScore: 0, qualityCutoffNotMet: false };
  const result = projectAuthorHistory({ isPopulated: true, items: [item, { id: 0 }] });
  assert.equal(result.items.length, 1);
  const history = result.items[0];
  assert.equal(history.bookId, 0);
  assert.equal(history.eventType, 'unknown');
  assert.equal(history.customFormatScore, 0);
  assert.equal(history.qualityCutoffNotMet, false);
  assert.equal(history.data, data);
  assert.deepEqual(history.customFormats, []);
  assert.equal(item.eventType, 99);
});

test('file editor limits selected identifiers to files still present and retains quality zero and metadata', () => {
  const files = editorFiles([{ id: 4, bookId: 8, size: 0, path: '/synthetic/one.m4b' }, { id: 7, bookId: 8, path: '/synthetic/two.m4b' }]);
  assert.deepEqual(selectedFileIds(files, { 4: true, 7: false, 9: true }), [4]);
  assert.equal(files[0].size, 0);
  const quality = { id: 0, name: 'Unknown', weight: 0 };
  assert.deepEqual(qualityChoices([{ quality }, { items: [{ quality: { id: 9, name: 'M4B' } }] }]), [quality, { id: 9, name: 'M4B' }]);
  const columns = editorColumns([{ name: 'quality', label: () => 'Quality', isVisible: false, isSortable: false }]);
  assert.equal(columns[0].label(), 'Quality');
  assert.equal(columns[0].isVisible, false);
  assert.equal(columns[0].isSortable, false);
});
