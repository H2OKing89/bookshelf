import assert from 'node:assert/strict';
import test from 'node:test';
import { loadFrontendModule } from './helpers/loadFrontendModule.mjs';

const boundaries = Object.fromEntries([
  'Components/Alert', 'Components/Form/FormGroup', 'Components/Form/FormInputHelpText', 'Components/Form/FormLabel',
  'Components/Link/Button', 'Components/Link/SpinnerButton', 'Components/Loading/LoadingIndicator',
  'Components/Modal/Modal', 'Components/Modal/ModalBody', 'Components/Modal/ModalContent',
  'Components/Modal/ModalFooter', 'Components/Modal/ModalHeader'
].map((name) => [name, () => null]));

function setup() {
  const requests = [];
  const created = [];
  let closeCount = 0;
  const { default: Modal } = loadFrontendModule('UnmappedFiles/CreateLocalRecordingModal.js', {
    ...boundaries,
    'react-redux': { connect: () => (component) => component },
    'Helpers/Props': { kinds: {}, sizes: {} },
    'Utilities/createAjaxRequest': (options) => {
      const pending = { options, aborted: false };
      requests.push(pending);
      return {
        request: {
          done(callback) { pending.resolve = callback; },
          fail(callback) { pending.reject = callback; }
        },
        abortRequest() { pending.aborted = true; }
      };
    }
  });
  const component = new Modal({
    isOpen: true,
    files: [{ id: 7, path: '/books/course/01.mp3' }, { id: 8, path: '/books/course/02.mp3' }],
    authors: [{ id: 4, authorName: 'Course Author' }],
    onModalClose() { closeCount++; },
    onCreated(book) { created.push(book); }
  });
  component.setState = (update) => {
    component.state = { ...component.state, ...(typeof update === 'function' ? update(component.state) : update) };
  };
  component.componentDidMount();
  return { component, requests, created, closeCount: () => closeCount };
}

const preview = {
  fileIds: [7, 8], title: 'Course', subtitle: 'Part One', authorId: 4, authorName: 'Course Author',
  narrators: ['Reader, First', 'Second Reader'], language: 'English', publisher: 'Publisher', year: 2016, warnings: []
};

test('previews the selected files and submits the reviewed metadata as one local recording', () => {
  const { component, requests, created, closeCount } = setup();
  assert.deepEqual(requests[0].options.data, { fileIds: [7, 8] });
  assert.equal(requests[0].options.traditional, true);
  requests[0].resolve(preview);
  assert.equal(component.state.fields.narrators, 'Reader, First\nSecond Reader');
  component.onInputChange({ name: 'title', value: '  Reviewed Course  ' });
  component.onInputChange({ name: 'year', value: '' });
  component.onCreatePress();
  assert.equal(requests.length, 2);
  assert.equal(requests[1].options.method, 'POST');
  assert.deepEqual(JSON.parse(requests[1].options.data), {
    fileIds: [7, 8], title: 'Reviewed Course', subtitle: 'Part One', authorId: 4,
    narrators: ['Reader, First', 'Second Reader'], language: 'English', publisher: 'Publisher', year: null
  });
  component.onCreatePress();
  component.onModalClose();
  assert.equal(requests.length, 2, 'double clicking must not issue another write');
  assert.equal(closeCount(), 0, 'do not discard the in-flight result');
  const book = { id: 23, titleSlug: 'local-course', title: 'Reviewed Course' };
  requests[1].resolve(book);
  assert.deepEqual(created, [book]);
  assert.equal(component.state.isSaving, false);
});

test('failed preview and save requests remain visible and retry preserves reviewed metadata', () => {
  const { component, requests, created } = setup();
  requests[0].reject({ status: 500 });
  assert.equal(component.state.error, 'Unable to read the selected files.');
  assert.equal(component.state.isLoading, false);
  component.onCreatePress();
  assert.equal(requests.length, 1, 'a failed preview cannot be submitted');
  component.onRetryPress();
  requests[1].resolve(preview);
  component.onInputChange({ name: 'title', value: 'Reviewed Course' });
  component.onCreatePress();
  requests[2].reject({ responseJSON: [{ errorMessage: 'A selected file was already linked.' }] });
  assert.equal(component.state.error, 'A selected file was already linked.');
  assert.equal(component.state.fields.title, 'Reviewed Course');
  assert.equal(component.state.isSaving, false);
  assert.deepEqual(created, []);
  component.onCreatePress();
  assert.equal(requests.length, 4);
});

test('closing or reopening cancels previews and ignores responses for the previous selection', () => {
  const { component, requests } = setup();
  const openProps = component.props;
  component.props = { ...openProps, isOpen: false };
  component.componentDidUpdate(openProps);
  assert.equal(requests[0].aborted, true);
  const closedProps = component.props;
  component.props = { ...closedProps, isOpen: true, files: [{ id: 9, path: '/books/other.mp3' }] };
  component.componentDidUpdate(closedProps);
  requests[0].resolve(preview);
  assert.equal(component.state.preview, null);
  requests[1].resolve({ ...preview, fileIds: [9], title: 'Other recording' });
  assert.equal(component.state.fields.title, 'Other recording');
  component.componentWillUnmount();
  assert.equal(requests[1].aborted, true);
  requests[1].resolve(preview);
  assert.equal(component.state.fields.title, 'Other recording');
});

test('creation requires a reviewed title, an existing author and a valid optional year', () => {
  const { component, requests } = setup();
  requests[0].resolve(preview);
  for (const [name, value] of [['title', ' '], ['authorId', 999], ['year', '2026oops'], ['year', '0']]) {
    component.setState({ fields: { ...component.state.fields, title: 'Course', authorId: 4, year: '', [name]: value } });
    component.onCreatePress();
    assert.ok(component.state.error);
    assert.ok(component.state.fieldErrors[name]);
    assert.equal(requests.length, 1);
  }
  const [label, content] = component.renderField('year', 'Year').props.children;
  const [input, error] = content.props.children.filter(Boolean);
  assert.equal(label.props.htmlFor, input.props.id);
  assert.equal(input.props['aria-invalid'], true);
  assert.equal(input.props['aria-describedby'], error.props.id);
});

test('Unmapped exposes selection without changing the shared file-table column settings', () => {
  const { default: connector } = loadFrontendModule('UnmappedFiles/UnmappedFilesTableConnector.js', {
    'react-redux': { connect: (createSelector, createDispatch) => (component) => ({ createSelector, createDispatch, component }) },
    'Commands/commandNames': { RESCAN_FOLDERS: 'RescanFolders' },
    'Components/withCurrentPage': (component) => component,
    'Store/Actions/baseActions': { updateItem: (payload) => ({ type: 'updateItem', payload }) },
    'Store/Actions/authorActions': { fetchAuthor: (payload) => ({ type: 'fetchAuthor', payload }) },
    'Store/Actions/bookActions': { fetchBooks: (payload) => ({ type: 'fetchBooks', payload }) },
    'Store/Actions/bookFileActions': { fetchBookFiles: (payload) => ({ type: 'fetchBookFiles', payload }) },
    'Store/Actions/commandActions': {},
    'Store/Selectors/createClientSideCollectionSelector': () => (state) => state.bookFiles,
    'Store/Selectors/createCommandExecutingSelector': () => () => false,
    'Utilities/pagePopulator': {},
    './UnmappedFilesTable': () => null
  });
  const columns = [{ name: 'select', isVisible: false }, { name: 'path', isVisible: true }];
  const state = {
    bookFiles: { columns, items: [{ id: 1, bookId: 0 }, { id: 2, bookId: 23 }] },
    app: { dimensions: { isSmallScreen: false } }
  };
  const result = connector.createSelector()(state);
  assert.equal(result.columns[0].isVisible, true);
  assert.equal(columns[0].isVisible, false);
  assert.deepEqual(result.items.map((file) => file.id), [1]);

  const actions = [];
  const created = {
    id: 23, authorId: 4, title: 'Local course', titleSlug: 'local-course',
    metadataLabels: ['Local recording']
  };
  connector.createDispatch((action) => actions.push(action)).onLocalRecordingCreated(created);
  assert.deepEqual(actions, [
    { type: 'updateItem', payload: { section: 'books', ...created } },
    { type: 'fetchBooks', payload: { authorId: 4 } },
    { type: 'fetchAuthor', payload: { id: 4 } },
    { type: 'fetchBookFiles', payload: { unmapped: true } }
  ]);
});
