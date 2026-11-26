import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function LoadingIndicator() {}
function FileDetails() {}

const { default: FileDetailsConnector } = loadFrontendModule('BookFile/FileDetailsConnector.js', {
  'react-redux': { connect: () => (component) => component },
  'Components/Loading/LoadingIndicator': LoadingIndicator,
  'Store/Actions/bookFileActions': { fetchBookFiles() {} },
  './FileDetails': FileDetails
});

test('file details shows completed request errors instead of waiting for tags', () => {
  for (const items of [[{ id: 1 }], []]) {
    const component = new FileDetailsConnector({
      id: 1, items, isFetching: false,
      error: { responseJSON: { message: 'The file cannot be read.' } }
    });
    const view = component.render();
    assert.equal(view.type, 'div');
    assert.equal(view.props.children, 'The file cannot be read.');
  }
});

test('file details handles vanished files, tagless files, loading and successful reads', () => {
  const render = (props) => new FileDetailsConnector({
    id: 1, items: [], isFetching: false, error: null, ...props
  }).render();
  assert.equal(render({}).props.children, 'This file is no longer available.');
  assert.equal(render({ items: [{ id: 1 }] }).props.children, 'No audio tags are available for this file.');
  assert.equal(render({ isFetching: true }).type, LoadingIndicator);
  assert.equal(render({ error: { status: 500 } }).props.children, 'Unable to load file details');
  const audioTags = { title: 'The recording' };
  const success = render({ items: [{ id: 1, path: '/books/recording.m4b', audioTags }] });
  assert.equal(success.type, FileDetails);
  assert.equal(success.props.audioTags, audioTags);
  assert.equal(success.props.filename, '/books/recording.m4b');
});

const tableBoundaries = Object.fromEntries([
  'Components/Alert', 'Components/Form/SelectInput', 'Components/Form/TextInput', 'Components/Link/SpinnerButton',
  'Components/Loading/LoadingIndicator', 'Components/Modal/ConfirmModal',
  'Components/Table/Table', 'Components/Table/TableBody', './BookFileEditorRow'
].map((name) => [name, () => null]));
const { default: BookFileEditorTableContent } = loadFrontendModule('BookFile/Editor/BookFileEditorTableContent.js', {
  ...tableBoundaries,
  'Helpers/Props': { kinds: {} },
  'Utilities/String/translate': (key) => key
});

function fileTable(ids) {
  const component = new BookFileEditorTableContent({ items: ids.map((id) => ({ id })) });
  component.setState = (update) => {
    component.state = { ...component.state, ...(typeof update === 'function' ? update(component.state) : update) };
  };
  // TableSelectCell registers each mounted row through this same callback.
  ids.forEach((id) => component.onSelectedChange({ id, value: false }));
  return component;
}

function updateFiles(component, ids) {
  const previousProps = component.props;
  component.props = { ...previousProps, items: ids.map((id) => ({ id })) };
  component.componentDidUpdate(previousProps);
}

test('file deletion preserves surviving selections and Select All still selects every remaining row', () => {
  const table = fileTable([1, 2, 3]);
  table.onSelectedChange({ id: 2, value: true });
  updateFiles(table, [2, 3]);
  assert.deepEqual(table.getSelectedIds(), [2]);
  assert.deepEqual(table.state.selectedState, { 2: true, 3: false });
  table.onSelectAllChange({ value: true });
  assert.deepEqual(table.getSelectedIds(), [2, 3]);
  table.onSelectAllChange({ value: false });
  assert.deepEqual(table.getSelectedIds(), []);
});

test('new file rows retain registration and removing the shift-selection anchor is safe', () => {
  const table = fileTable([1, 2]);
  table.onSelectedChange({ id: 1, value: true });
  table.onSelectedChange({ id: 3, value: false });
  updateFiles(table, [1, 2, 3]);
  table.onSelectAllChange({ value: true });
  assert.deepEqual(table.getSelectedIds(), [1, 2, 3]);
  table.onSelectedChange({ id: 1, value: false });
  updateFiles(table, [2, 3]);
  assert.equal(table.state.lastToggled, null);
  assert.doesNotThrow(() => table.onSelectedChange({ id: 3, value: false, shiftKey: true }));
  assert.deepEqual(table.getSelectedIds(), [2]);
});

const { default: seriesConnector } = loadFrontendModule('Author/Details/AuthorDetailsSeriesConnector.js', {
  'react-redux': { connect: (createSelector) => (component) => ({ createSelector, component }) },
  'Store/Actions/bookActions': { toggleBooksMonitored() {} },
  'Store/Actions/commandActions': { executeCommand() {} },
  'Store/Actions/seriesActions': { setSeriesSort() {}, setSeriesTableOption() {} },
  './AuthorDetailsSeries': () => null
});

test('series Number sorting supports both directions, numeric values and alternate recordings', () => {
  const books = [{ id: 10 }, { id: 2 }, { id: 1 }, { id: 3 }, { id: 4 }];
  const state = {
    books: { items: books },
    authors: { items: [{ id: 1, monitored: true }], itemMap: { 1: 0 } },
    series: {
      items: [{ id: 1, title: 'Series', links: [
        { bookId: 1, position: '1' }, { bookId: 2, position: '2' },
        { bookId: 3, position: '2.5' }, { bookId: 4, position: '2' }, { bookId: 10, position: '10' }
      ] }], sortKey: 'position', sortDirection: 'ascending', columns: []
    },
    commands: { items: [] }, app: { dimensions: { isSmallScreen: false } }, settings: { ui: { item: {} } }
  };
  const select = seriesConnector.createSelector();
  assert.deepEqual(select(state, { authorId: 1, seriesId: 1 }).items.map((item) => item.id), [1, 2, 4, 3, 10]);
  const descending = { ...state, series: { ...state.series, sortDirection: 'descending' } };
  assert.deepEqual(select(descending, { authorId: 1, seriesId: 1 }).items.map((item) => item.id), [10, 3, 2, 4, 1]);
  assert.deepEqual(books.map((item) => item.id), [10, 2, 1, 3, 4]);
});

test('book details renders the local-recording source badge supplied by the API', () => {
  function Label() {}
  const boundaries = Object.fromEntries([
    'Author/AuthorNameLink', 'Book/BookCover', 'Components/Alert', 'Components/HeartRating',
    'Components/Icon', 'Components/Marquee', 'Components/Measure', 'Components/MonitorToggleButton',
    'Components/Tooltip/Tooltip', 'Components/TextTruncate', './BookDetailsLinks'
  ].map((name) => [name, () => null]));
  const { default: Header } = loadFrontendModule('Book/Details/BookDetailsHeader.js', {
    ...boundaries,
    'Components/Label': Label,
    'Helpers/Props': { icons: {}, kinds: {}, sizes: {}, tooltipPositions: {} },
    'Styles/Variables/fonts': { defaultFontSize: '14', lineHeight: '1.5' },
    'Utilities/String/translate': (key) => key
  });
  const props = {
    id: 23, title: 'Local course', titleSlug: 'local-course', metadataLabels: ['Local recording'],
    ratings: { value: 0 }, images: [], links: [], author: { images: [], authorName: 'Course Author' },
    isSmallScreen: true, monitored: false
  };
  const labels = (view) => {
    if (Array.isArray(view)) {
      return view.flatMap(labels);
    }
    if (!view || typeof view !== 'object') {
      return [];
    }
    return view.type === Label ? [view.props.children] : labels(view.props?.children);
  };
  assert.equal(labels(new Header(props).render()).filter((label) => label === 'Local recording').length, 1);
  assert.equal(labels(new Header({ ...props, metadataLabels: [] }).render()).includes('Local recording'), false);
});
