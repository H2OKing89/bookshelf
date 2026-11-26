import moment from 'moment';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { libraryResponseParser, parseLibraryBook, parseLibraryBookFile } from 'Book/LibraryModels';
import { sortDirections } from 'Helpers/Props';
import { asStateRecord, createItemMap, isStateRecord, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import updateSectionState from 'Utilities/State/updateSectionState';
import naturalExpansion from 'Utilities/String/naturalExpansion';
import { set, update, updateItem } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import { requestLegacy } from './Creators/legacyRequest';
import { isLibraryId, numberValue, textValue } from './Creators/libraryActionsState';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import { createSectionRequests } from './Creators/sectionRequests';

//
// Variables

export const section = 'interactiveImport';

const booksSection = `${section}.books`;
const bookFilesSection = `${section}.bookFiles`;
const reads = createSectionRequests();
const writes = createSectionRequests();
const previousIds = new WeakMap<() => LegacyRootState, number[]>();

const MAXIMUM_RECENT_FOLDERS = 10;

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  isSaving: false,
  error: null,
  items: [],
  pendingChanges: {},
  sortKey: 'path',
  sortDirection: sortDirections.ASCENDING,
  secondarySortKey: 'path',
  secondarySortDirection: sortDirections.ASCENDING,
  recentFolders: [],
  importMode: 'chooseImportMode',
  sortPredicates: {
    path: (item: StateRecord) => naturalExpansion(textValue(item.path).toLowerCase()),
    author: (item: StateRecord) => textValue(asStateRecord(item.author).sortName),
    quality: (item: StateRecord) => numberValue(item.qualityWeight)
  },

  books: {
    isFetching: false,
    isPopulated: false,
    error: null,
    sortKey: 'title',
    sortDirection: sortDirections.ASCENDING,
    items: []
  },

  bookFiles: {
    isFetching: false,
    isPopulated: false,
    error: null,
    sortKey: 'relativePath',
    sortDirection: sortDirections.ASCENDING,
    items: []
  }
};

export const persistState = [
  'interactiveImport.sortKey',
  'interactiveImport.sortDirection',
  'interactiveImport.recentFolders',
  'interactiveImport.importMode'
];

//
// Actions Types

export const FETCH_INTERACTIVE_IMPORT_ITEMS = 'interactiveImport/fetchInteractiveImportItems';
export const SAVE_INTERACTIVE_IMPORT_ITEM = 'interactiveImport/saveInteractiveImportItem';
export const SET_INTERACTIVE_IMPORT_SORT = 'interactiveImport/setInteractiveImportSort';
export const UPDATE_INTERACTIVE_IMPORT_ITEM = 'interactiveImport/updateInteractiveImportItem';
export const UPDATE_INTERACTIVE_IMPORT_ITEMS = 'interactiveImport/updateInteractiveImportItems';
export const CLEAR_INTERACTIVE_IMPORT = 'interactiveImport/clearInteractiveImport';
export const ADD_RECENT_FOLDER = 'interactiveImport/addRecentFolder';
export const REMOVE_RECENT_FOLDER = 'interactiveImport/removeRecentFolder';
export const SET_INTERACTIVE_IMPORT_MODE = 'interactiveImport/setInteractiveImportMode';

export const FETCH_INTERACTIVE_IMPORT_BOOKS = 'interactiveImport/fetchInteractiveImportBooks';
export const SET_INTERACTIVE_IMPORT_BOOKS_SORT = 'interactiveImport/clearInteractiveImportBooksSort';
export const CLEAR_INTERACTIVE_IMPORT_BOOKS = 'interactiveImport/clearInteractiveImportBooks';

export const FETCH_INTERACTIVE_IMPORT_TRACKFILES = 'interactiveImport/fetchInteractiveImportBookFiles';
export const CLEAR_INTERACTIVE_IMPORT_TRACKFILES = 'interactiveImport/clearInteractiveImportBookFiles';

//
// Action Creators

export const fetchInteractiveImportItems = createThunk(FETCH_INTERACTIVE_IMPORT_ITEMS);
export const setInteractiveImportSort = createAction<StateRecord>(SET_INTERACTIVE_IMPORT_SORT);
export const updateInteractiveImportItem = createAction<StateRecord>(UPDATE_INTERACTIVE_IMPORT_ITEM);
export const updateInteractiveImportItems = createAction<StateRecord>(UPDATE_INTERACTIVE_IMPORT_ITEMS);
export const saveInteractiveImportItem = createThunk(SAVE_INTERACTIVE_IMPORT_ITEM);
export const clearInteractiveImport = createThunk(CLEAR_INTERACTIVE_IMPORT);
const clearImportAction = createAction(CLEAR_INTERACTIVE_IMPORT);
export const addRecentFolder = createAction<StateRecord>(ADD_RECENT_FOLDER);
export const removeRecentFolder = createAction<StateRecord>(REMOVE_RECENT_FOLDER);
export const setInteractiveImportMode = createAction<StateRecord>(SET_INTERACTIVE_IMPORT_MODE);

export const fetchInteractiveImportBooks = createThunk(FETCH_INTERACTIVE_IMPORT_BOOKS);
export const setInteractiveImportBooksSort = createAction<StateRecord>(SET_INTERACTIVE_IMPORT_BOOKS_SORT);
export const clearInteractiveImportBooks = createAction(CLEAR_INTERACTIVE_IMPORT_BOOKS);

export const fetchInteractiveImportBookFiles = createThunk(FETCH_INTERACTIVE_IMPORT_TRACKFILES);
export const clearInteractiveImportBookFiles = createAction(CLEAR_INTERACTIVE_IMPORT_TRACKFILES);

function manualId(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 && value <= 2147483647;
}

type ManualRow = StateRecord & { id: number; path: string };

function isManualRow(value: unknown): value is ManualRow {
  return isStateRecord(value) && manualId(value.id) && typeof value.path === 'string' && value.path.length > 0;
}

function manualRows(value: unknown): ManualRow[] {
  return Array.isArray(value) ? value.filter(isManualRow) : [];
}

function parseManualRows(value: unknown): ManualRow[] {
  if (!Array.isArray(value) || !value.every(isManualRow) || new Set(value.map((item) => item.id)).size !== value.length) {
    throw new TypeError('Invalid import rows');
  }
  return value;
}

function resetReprocessing(ids: readonly number[]) {
  return ids.map((id) => updateItem({ section, id, isReprocessing: false, updateOnly: true }));
}

export const actionHandlers = handleThunks({
  [FETCH_INTERACTIVE_IMPORT_ITEMS]: (getState, payload, dispatch) => {
    const download = typeof payload.downloadId === 'string' && payload.downloadId.length > 0;
    const folder = typeof payload.folder === 'string' && payload.folder.length > 0;
    const attempt = reads.begin(getState, section);
    writes.cancel(getState, section);
    dispatch(batchActions(resetReprocessing(previousIds.get(getState) ?? [])));
    previousIds.delete(getState);

    if (!download && !folder) {
      attempt.finish();
      dispatch(set({ section, isFetching: false, isPopulated: false, error: { message: '`downloadId` or `folder` is required.' } }));
      return;
    }
    dispatch(set({ section, isFetching: true }));
    const { request, abortRequest } = requestLegacy({ url: '/manualimport', data: payload });
    attempt.attachAbort(abortRequest);
    request.done((data) => {
      if (!attempt.finish()) {
        return;
      }
      try {
        dispatch(batchActions([
          update({ section, data: parseManualRows(data) }),
          set({ section, isFetching: false, isPopulated: true, error: null })
        ]));
      } catch {
        dispatch(set({ section, isFetching: false, isPopulated: false, error: new ApiError({ kind: 'invalid-response' }) }));
      }
    });
    request.fail((xhr) => {
      if (attempt.finish()) {
        dispatch(set({ section, isFetching: false, isPopulated: false, error: isStateRecord(xhr) && xhr.aborted === true ? null : xhr }));
      }
    });
  },

  [SAVE_INTERACTIVE_IMPORT_ITEM]: (getState, payload, dispatch) => {
    // One existing caller uses the singular key for a whole selection.
    const selected = payload.ids ?? payload.id;
    if (!Array.isArray(selected) || !selected.length || !selected.every(manualId)) {
      return;
    }
    const ids = [...new Set<number>(selected)];
    const items = manualRows(getState()[section]?.items);
    const selectedItems = items.filter((item) => ids.includes(item.id));
    if (selectedItems.length !== ids.length) {
      return;
    }
    const attempt = writes.begin(getState, section);
    dispatch(batchActions([
      ...resetReprocessing(previousIds.get(getState) ?? []),
      ...ids.map((id) => updateItem({ section, id, isReprocessing: true, updateOnly: true }))
    ]));
    previousIds.set(getState, ids);
    const data = selectedItems.map((item) => {
      const author = asStateRecord(item.author);
      const book = asStateRecord(item.book);
      return {
        id: item.id,
        path: item.path,
        authorId: isLibraryId(author.id) ? author.id : undefined,
        bookId: isLibraryId(book.id) ? book.id : undefined,
        foreignEditionId: item.foreignEditionId,
        quality: item.quality,
        releaseGroup: item.releaseGroupOverride,
        indexerFlags: item.indexerFlags,
        downloadId: item.downloadId,
        downloadClientId: item.downloadClientId,
        additionalFile: item.additionalFile,
        replaceExistingFiles: item.replaceExistingFiles,
        disableReleaseSwitching: item.disableReleaseSwitching
      };
    });
    const { request, abortRequest } = requestLegacy({ method: 'POST', url: '/manualimport', contentType: 'application/json', data: JSON.stringify(data) });
    attempt.attachAbort(abortRequest);
    const fail = () => {
      if (attempt.finish()) {
        previousIds.delete(getState);
        dispatch(batchActions(resetReprocessing(ids)));
      }
    };
    request.done((response) => {
      let rows: ManualRow[] = [];
      try {
        rows = parseManualRows(response);
        if (rows.length !== ids.length || rows.some((row) => !selectedItems.some((item) => item.id === row.id && item.path === row.path))) {
          throw new TypeError('Invalid import response identity');
        }
      } catch {
        fail();
        return;
      }
      if (attempt.finish()) {
        previousIds.delete(getState);
        dispatch(batchActions(rows.map((item) => updateItem({ ...item, section, isReprocessing: false, updateOnly: true }))));
      }
    });
    request.fail(fail);
  },

  [CLEAR_INTERACTIVE_IMPORT]: (getState, _payload, dispatch) => {
    reads.cancel(getState, section);
    writes.cancel(getState, section);
    previousIds.delete(getState);
    dispatch(clearImportAction());
  },

  [FETCH_INTERACTIVE_IMPORT_BOOKS]: createFetchHandler(booksSection, '/book', libraryResponseParser(parseLibraryBook)),

  [FETCH_INTERACTIVE_IMPORT_TRACKFILES]: createFetchHandler(bookFilesSection, '/bookFile', libraryResponseParser(parseLibraryBookFile))
});

//
// Reducers

export const reducers = createHandleActions({

  [UPDATE_INTERACTIVE_IMPORT_ITEM]: (state, { payload }) => {
    if (!isStateRecord(payload) || !manualId(payload.id)) {
      return state;
    }
    const items = manualRows(state.items);
    if (!items.some((item) => item.id === payload.id)) {
      return state;
    }
    const updated = items.map((item) => item.id === payload.id ? { ...item, ...payload, id: item.id } : item);
    return { ...state, items: updated, itemMap: createItemMap(updated) };
  },

  [UPDATE_INTERACTIVE_IMPORT_ITEMS]: (state, { payload }) => {
    if (!isStateRecord(payload) || !Array.isArray(payload.ids) || !payload.ids.every(manualId)) {
      return state;
    }
    const { ids, ...fields } = payload;
    const items = manualRows(state.items);
    const updated = items.map((item) => ids.includes(item.id) ? { ...item, ...fields, id: item.id } : item);
    return { ...state, items: updated, itemMap: createItemMap(updated) };
  },

  [ADD_RECENT_FOLDER]: (state, { payload }) => {
    if (!isStateRecord(payload) || typeof payload.folder !== 'string' || !payload.folder) {
      return state;
    }
    const folder = payload.folder;
    const folders: unknown[] = Array.isArray(state.recentFolders) ? state.recentFolders : [];
    const recentFolders = folders.filter((item) => isStateRecord(item) && item.folder !== folder);
    recentFolders.push({ folder, lastUsed: moment().toISOString() });
    return { ...state, recentFolders: recentFolders.slice(-MAXIMUM_RECENT_FOLDERS) };
  },

  [REMOVE_RECENT_FOLDER]: (state, { payload }) => {
    if (!isStateRecord(payload) || typeof payload.folder !== 'string') {
      return state;
    }
    const items: unknown[] = Array.isArray(state.recentFolders) ? state.recentFolders : [];
    return { ...state, recentFolders: items.filter((item) => !isStateRecord(item) || item.folder !== payload.folder) };
  },

  [CLEAR_INTERACTIVE_IMPORT]: function(state) {
    const newState = {
      ...defaultState,
      itemMap: {},
      recentFolders: state.recentFolders,
      importMode: state.importMode
    };

    return newState;
  },

  [SET_INTERACTIVE_IMPORT_SORT]: createSetClientSideCollectionSortReducer(section),

  [SET_INTERACTIVE_IMPORT_MODE]: function(state, { payload }) {
    return isStateRecord(payload) && typeof payload.importMode === 'string' ? { ...state, importMode: payload.importMode } : state;
  },

  [SET_INTERACTIVE_IMPORT_BOOKS_SORT]: createSetClientSideCollectionSortReducer(booksSection),

  [CLEAR_INTERACTIVE_IMPORT_BOOKS]: (state) => {
    return updateSectionState(state, booksSection, {
      ...defaultState.books
    });
  },

  [CLEAR_INTERACTIVE_IMPORT_TRACKFILES]: (state) => {
    return updateSectionState(state, bookFilesSection, {
      ...defaultState.bookFiles
    });
  }

}, defaultState, section);
