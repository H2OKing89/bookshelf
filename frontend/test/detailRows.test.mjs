import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const noop = () => null;
const { groupBookFiles } = loadFrontendModule('Author/Details/BookRowConnector.tsx', {
  './BookRow': noop,
  'react-redux': { connect: () => (Component) => Component },
  'Store/Selectors/createAuthorSelector': () => noop
});

test('author detail rows group only associated files without changing file order or dropping zero flags', () => {
  const items = [
    { id: 1, bookId: 0, indexerFlags: 0 },
    { id: 2, bookId: 10, indexerFlags: 0 },
    { id: 3, bookId: 20, indexerFlags: 7 },
    { id: 4, bookId: 10, indexerFlags: 2 }
  ];
  const groups = groupBookFiles(items);
  assert.equal(groups.has(0), false);
  assert.deepEqual(groups.get(10), [items[1], items[3]]);
  assert.equal(groups.get(10)[0].indexerFlags, 0);
  assert.deepEqual(groups.get(20), [items[2]]);
});

const { default: BookStatus } = loadFrontendModule('Author/Details/BookStatus.tsx', {
  'Book/BookQuality': noop,
  'Components/Label': noop,
  'Helpers/Props': { kinds: { INFO: 'info', WARNING: 'warning', DANGER: 'danger' } },
  'Utilities/String/translate': (key) => key
});

test('an associated file without quality metadata is not rendered as a missing recording', () => {
  const child = BookStatus({ bookFile: { id: 1, quality: null }, monitored: true, isAvailable: true }).props.children;
  assert.equal(child.type, noop);
  assert.equal(child.props.quality, null);
  assert.equal(child.props.isCutoffNotMet, false);
  const missing = BookStatus({ monitored: true, isAvailable: true, recordingAsin: 'B000000000' }).props.children;
  assert.equal(missing.props.children, 'RecordingMissing');
});
