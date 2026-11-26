import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { libraryResponseParser, parseLibraryBookFile } from 'Book/LibraryModels';
import { sortDirections } from 'Helpers/Props';
import { type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { bookFileQualityRank } from 'Utilities/Quality/qualityRank';
import { removeItem, set, updateItem } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createRemoveItemHandler from './Creators/createRemoveItemHandler';
import { requestLegacy } from './Creators/legacyRequest';
import { bookSection, isLibraryId, libraryIds, parseExactRows, sectionRows } from './Creators/libraryActionsState';
import createClearReducer from './Creators/Reducers/createClearReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

//
// Variables

export const section = 'bookFiles';

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  sortKey: 'path',
  sortDirection: sortDirections.ASCENDING,

  error: null,
  isDeleting: false,
  deleteError: null,
  isSaving: false,
  saveError: null,
  items: [],

  sortPredicates: {
    quality: bookFileQualityRank
  },

  columns: [
    {
      name: 'select',
      columnLabel: 'Select',
      isSortable: false,
      isVisible: false,
      isModifiable: false,
      isHidden: true
    },
    {
      name: 'path',
      label: 'Path',
      isSortable: true,
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'size',
      label: 'Size',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'dateAdded',
      label: 'Date Added',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'quality',
      label: 'Quality',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'releaseGroup',
      label: 'Release Group',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'actions',
      columnLabel: 'Actions',
      isVisible: true,
      isModifiable: false
    }
  ]
};

export const persistState = [
  'bookFiles.sortKey',
  'bookFiles.sortDirection'
];

//
// Actions Types

export const FETCH_BOOK_FILES = 'bookFiles/fetchBookFiles';
export const DELETE_BOOK_FILE = 'bookFiles/deleteBookFile';
export const DELETE_BOOK_FILES = 'bookFiles/deleteBookFiles';
export const UPDATE_BOOK_FILES = 'bookFiles/updateBookFiles';
export const SET_BOOK_FILES_SORT = 'bookFiles/setBookFilesSort';
export const SET_BOOK_FILES_TABLE_OPTION = 'bookFiles/setBookFilesTableOption';
export const CLEAR_BOOK_FILES = 'bookFiles/clearBookFiles';

//
// Action Creators

export const fetchBookFiles = createThunk(FETCH_BOOK_FILES);
export const deleteBookFile = createThunk(DELETE_BOOK_FILE);
export const deleteBookFiles = createThunk(DELETE_BOOK_FILES);
export const updateBookFiles = createThunk(UPDATE_BOOK_FILES);
export const setBookFilesSort = createAction<{ sortKey?: string; sortDirection?: 'ascending' | 'descending' }>(SET_BOOK_FILES_SORT);
export const setBookFilesTableOption = createAction<StateRecord>(SET_BOOK_FILES_TABLE_OPTION);
export const clearBookFiles = createAction(CLEAR_BOOK_FILES);

//
// Helpers

const deleteBookFileHelper = createRemoveItemHandler(section, '/bookFile');

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_BOOK_FILES]: createFetchHandler(section, '/bookFile', libraryResponseParser(parseLibraryBookFile)),

  [DELETE_BOOK_FILE]: (getState, payload, dispatch) => {
    const { id, bookEntity, ...parameters } = payload;
    const target = bookSection(bookEntity);
    if (!isLibraryId(id) || target === null) {
      dispatch(set({ section, isDeleting: false, deleteError: new ApiError({ kind: 'invalid-request' }) }));
      return;
    }
    const promise = deleteBookFileHelper(getState, { id, ...parameters }, dispatch);
    promise?.done(() => {
      const books = sectionRows(getState, 'books').filter((book) => book.bookFileId === id);
      dispatch(batchActions(books.map((book) => updateItem({ section: target, id: book.id, bookFileId: 0, hasFile: false, updateOnly: true }))));
    });
  },

  [DELETE_BOOK_FILES]: (getState, payload, dispatch) => {
    const ids = libraryIds(payload.bookFileIds);
    if (!ids) {
      dispatch(set({ section, isDeleting: false, deleteError: new ApiError({ kind: 'invalid-request' }) }));
      return;
    }
    dispatch(set({ section, isDeleting: true }));
    const { request } = requestLegacy({ url: '/bookFile/bulk', method: 'DELETE', dataType: 'json', data: JSON.stringify({ bookFileIds: ids }) });
    request.done(() => {
      const books = sectionRows(getState, 'books').filter((book) => isLibraryId(book.bookFileId) && ids.includes(book.bookFileId));
      dispatch(batchActions([
        ...ids.map((id) => removeItem({ section, id })),
        ...books.map((book) => updateItem({ section: 'books', id: book.id, bookFileId: 0, hasFile: false, updateOnly: true })),
        set({ section, isDeleting: false, deleteError: null })
      ]));
    });
    request.fail((xhr) => dispatch(set({ section, isDeleting: false, deleteError: xhr })));
  },

  [UPDATE_BOOK_FILES]: (_getState, payload, dispatch) => {
    const ids = libraryIds(payload.bookFileIds);
    const { quality, releaseGroup } = payload;
    const fail = (error: unknown) => dispatch(set({ section, isSaving: false, saveError: error }));
    if (!ids || (releaseGroup != null && typeof releaseGroup !== 'string')) {
      fail(new ApiError({ kind: 'invalid-request' }));
      return;
    }
    const data = { bookFileIds: ids, ...(quality != null && { quality }), ...(releaseGroup != null && { releaseGroup }) };
    dispatch(set({ section, isSaving: true }));
    const { request } = requestLegacy({ url: '/bookFile/editor', method: 'PUT', dataType: 'json', data: JSON.stringify(data) });
    request.done((response) => {
      try {
        const files = parseExactRows(response, ids, parseLibraryBookFile);
        dispatch(batchActions([
          ...files.map((file) => updateItem({ ...file, section, updateOnly: true })),
          set({ section, isSaving: false, saveError: null })
        ]));
      } catch {
        fail(new ApiError({ kind: 'invalid-response' }));
      }
    });
    request.fail(fail);
  }
});

//
// Reducers

export const reducers = createHandleActions({
  [SET_BOOK_FILES_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_BOOK_FILES_TABLE_OPTION]: createSetTableOptionReducer(section),

  [CLEAR_BOOK_FILES]: createClearReducer(section, {
    isFetching: false,
    isPopulated: false,
    error: null,
    items: [],
    itemMap: {}
  })

}, defaultState, section);
