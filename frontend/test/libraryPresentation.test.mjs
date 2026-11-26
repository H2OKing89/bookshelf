import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

function stub() { return null; }
const translate = (value) => value;
const props = {
  icons: { REFRESH: {}, SEARCH: {}, EDIT: {} },
  inputTypes: { SELECT: 'select' }
};
const EditAuthor = () => null;
const DeleteAuthor = () => null;

function find(element, type) {
  if (Array.isArray(element)) {
    for (const child of element) {
      const result = find(child, type);
      if (result) { return result; }
    }
  } else if (element && typeof element === 'object') {
    if (element.type === type) { return element; }
    return find(element.props?.children, type);
  }
}

test('the book overview edits and deletes the owning author, and selects the book independently', () => {
  const { default: Overview } = loadFrontendModule('Book/Index/Overview/BookIndexOverview', {
    'Components/TextTruncate': stub,
    'Author/AuthorPoster': stub,
    'Author/Delete/DeleteAuthorModal': DeleteAuthor,
    'Author/Edit/EditAuthorModalConnector': EditAuthor,
    'Book/Index/ProgressBar/BookIndexProgressBar': stub,
    'Components/Form/CheckInput': stub,
    'Components/Link/IconButton': stub,
    'Components/Link/Link': stub,
    'Components/Link/SpinnerIconButton': stub,
    'Helpers/Props': props,
    'Styles/Variables/dimensions': { authorIndexColumnPadding: '8', authorIndexColumnPaddingSmallScreen: '4' },
    'Styles/Variables/fonts': { defaultFontSize: '12', lineHeight: '1.5' },
    'Utilities/createAjaxRequest': stub,
    'Utilities/String/stripHtml': (value) => value,
    'Utilities/String/translate': translate,
    './BookIndexOverviewInfo': stub
  });
  const selections = [];
  const item = new Overview({
    id: 42, authorId: 7, author: { id: 7 }, title: 'Fixture', titleSlug: 'fixture', monitored: false,
    statistics: { bookCount: 0, bookFileCount: 0 }, images: [], posterWidth: 100, posterHeight: 120,
    overviewOptions: {}, rowHeight: 160, onSelectedChange: (selection) => selections.push(selection)
  });
  item.setState = (patch) => { item.state = { ...item.state, ...patch }; };
  item.onEditAuthorPress();
  assert.equal(find(item.render(), EditAuthor).props.authorId, 7);
  assert.equal(find(item.render(), EditAuthor).props.isOpen, true);
  item.onDeleteAuthorPress();
  assert.equal(find(item.render(), EditAuthor).props.isOpen, false);
  assert.equal(find(item.render(), DeleteAuthor).props.authorId, 7);
  assert.equal(find(item.render(), DeleteAuthor).props.isOpen, true);
  item.onChange({ value: false, shiftKey: true });
  assert.deepEqual(selections, [{ id: 42, value: false, shiftKey: true }]);
});

test('edition selector keeps marketplace and local foreign IDs distinct and ignores foreign-book or numeric choices', () => {
  const Input = () => null;
  const { default: Row } = loadFrontendModule('InteractiveImport/Edition/SelectEditionRow', {
    'Components/Form/FormInputGroup': Input,
    'Components/Table/Cells/TableRowCell': stub,
    'Components/Table/TableRow': stub,
    'Helpers/Props': props
  });
  const changes = [];
  const row = new Row({
    id: 42, matchedEditionId: 'us-asin', title: 'Fixture',
    editions: [
      { id: 1, bookId: 42, foreignEditionId: 'us-asin', asin: 'SHAREDASIN', audibleRegion: 'us', title: 'Fixture recording' },
      { id: 2, bookId: 42, foreignEditionId: 'uk-asin', asin: 'SHAREDASIN', audibleRegion: 'uk', title: 'Fixture recording' },
      { id: 3, bookId: 42, foreignEditionId: 'local-id', asin: null, title: 'Fixture Local' },
      { id: 4, bookId: 42, foreignEditionId: 'ebook-id', isEbook: true, isbn13: '9781234567890', format: 'EPUB', pageCount: 300, title: 'Fixture EBook' },
      { id: 5, bookId: 43, foreignEditionId: 'other-book', title: 'Other book' }
    ],
    columns: [{ name: 'book', isVisible: true }, { name: 'edition', isVisible: true }],
    onEditionSelect: (...change) => changes.push(change)
  });
  const control = find(row.render(), Input).props;
  assert.deepEqual(control.values.map(({ key }) => key).sort(), ['ebook-id', 'local-id', 'uk-asin', 'us-asin']);
  assert.match(control.values.find(({ key }) => key === 'us-asin').value, /SHAREDASIN, US/);
  assert.match(control.values.find(({ key }) => key === 'uk-asin').value, /SHAREDASIN, UK/);
  assert.match(control.values.find(({ key }) => key === 'ebook-id').value, /9781234567890, EPUB, 300p/);
  for (const value of ['uk-asin', 'local-id', 'ebook-id', 1, 'other-book', 'unknown']) {
    row.onInputChange({ name: '42', value });
  }
  row.onInputChange({ name: '43', value: 'us-asin' });
  assert.deepEqual(changes, [[42, 'uk-asin'], [42, 'local-id'], [42, 'ebook-id']]);
});

test('recording metadata renders zero duration and an explicit false adult flag', () => {
  const { default: Metadata } = loadFrontendModule('Book/Details/BookRecordingMetadata', {
    'Utilities/String/translate': translate
  });
  const tree = Metadata({
    book: { isAdult: true, releaseDateIsPlaceholder: true, copyrightYear: 2000 },
    edition: { isAdult: false, releaseDateIsPlaceholder: false, copyrightYear: 0, chapterMetadata: { runtimeLengthMs: 0, chapters: [] } }
  });
  // Inspect the real element tree without a DOM or provider requests.
  const children = tree.props.children;
  assert.equal(children[0], false);
  const fields = children[1].props.children;
  const values = new Map(fields.map(({ props: { children: [label, value] } }) => [label.props.children, value.props.children]));
  assert.equal(values.get('AdultContent'), 'No');
  assert.equal(values.get('CopyrightYear'), 0);
  assert.equal(values.get('Duration'), '0:00:00');
});

test('file details lets evidence render without track numbers, country or embedded tags', () => {
  const DetailsItem = () => null;
  const { default: Details } = loadFrontendModule('BookFile/FileDetails', {
    'Components/DescriptionList/DescriptionList': stub,
    'Components/DescriptionList/DescriptionListItem': DetailsItem,
    'Components/DescriptionList/DescriptionListItemDescription': stub,
    'Components/DescriptionList/DescriptionListItemTitle': stub,
    'Components/Link/Link': stub,
    'Utilities/String/translate': translate
  });
  const item = Details({ audioTags: { country: null, trackNumbers: null }, releaseGroupEvidence: { recordingId: 'local-fixture', source: 'recordingFile' } });
  assert.equal(find(item, DetailsItem).props.data, '—');
  assert.doesNotThrow(() => Details({ audioTags: null }));
  assert.doesNotThrow(() => Details({ releaseGroupEvidence: { source: 'toString' } }));
});
