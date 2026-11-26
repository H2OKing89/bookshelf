import _ from 'lodash';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { parseLibraryAuthor, parseLibraryBook } from 'Book/LibraryModels';
import { asStateRecord, isStateRecord, type LegacyDispatch,type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import getNewAuthor from 'Utilities/Author/getNewAuthor';
import monitorNewItemsOptions from 'Utilities/Author/monitorNewItemsOptions';
import monitorOptions from 'Utilities/Author/monitorOptions';
import getNewBook from 'Utilities/Book/getNewBook';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';
import { set, update, updateItem } from './baseActions';
import createHandleActions from './Creators/createHandleActions';
import { requestLegacy } from './Creators/legacyRequest';
import { isLibraryId, sectionRows } from './Creators/libraryActionsState';
import { createSectionRequests } from './Creators/sectionRequests';

//
// Variables

export const section = 'search';
const reads = createSectionRequests();
const addAttempts = new WeakMap<() => LegacyRootState, object>();

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  isAdding: false,
  isAdded: false,
  addError: null,
  items: [],

  authorDefaults: {
    rootFolderPath: '',
    monitor: monitorOptions[0].key,
    monitorNewItems: monitorNewItemsOptions[0].key,
    qualityProfileId: 0,
    metadataProfileId: 0,
    tags: []
  },

  bookDefaults: {
    rootFolderPath: '',
    monitor: monitorOptions[0].key,
    monitorNewItems: monitorNewItemsOptions[0].key,
    qualityProfileId: 0,
    metadataProfileId: 0,
    tags: []
  }
};

export const persistState = [
  'search.bookDefaults',
  'search.authorDefaults'
];

//
// Actions Types

export const GET_SEARCH_RESULTS = 'search/getSearchResults';
export const ADD_AUTHOR = 'search/addAuthor';
export const ADD_BOOK = 'search/addBook';
export const CLEAR_SEARCH_RESULTS = 'search/clearSearchResults';
export const SET_AUTHOR_ADD_DEFAULT = 'search/setAuthorAddDefault';
export const SET_BOOK_ADD_DEFAULT = 'search/setBookAddDefault';

//
// Action Creators

export const getSearchResults = createThunk(GET_SEARCH_RESULTS);
export const addAuthor = createThunk(ADD_AUTHOR);
export const addBook = createThunk(ADD_BOOK);
export const clearSearchResults = createThunk(CLEAR_SEARCH_RESULTS);
const clearResultAction = createAction(CLEAR_SEARCH_RESULTS);
export const setAuthorAddDefault = createAction<StateRecord>(SET_AUTHOR_ADD_DEFAULT);
export const setBookAddDefault = createAction<StateRecord>(SET_BOOK_ADD_DEFAULT);

//
// Action Handlers

function parseSearchResponse(value: unknown) {
  if (!Array.isArray(value) || !value.every((item: unknown) => isStateRecord(item) &&
      isLibraryId(item.id) && typeof item.foreignId === 'string' && item.foreignId.length > 0 &&
      (isStateRecord(item.author) || isStateRecord(item.book))) ||
      new Set(value.map((item) => item.id)).size !== value.length) {
    throw new TypeError('Invalid search results');
  }
  return value;
}

function addResult(getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch, kind: 'author' | 'book'): void {
  const foreignId = kind === 'author' ? payload.foreignAuthorId : payload.foreignBookId;
  const item = sectionRows(getState, section).find((candidate) => candidate.foreignId === foreignId && isStateRecord(candidate[kind]));
  const invalid = () => dispatch(set({ section, isAdding: false, isAdded: false, addError: new ApiError({ kind: 'invalid-request' }) }));
  if (typeof foreignId !== 'string' || !foreignId || !item || !isStateRecord(item[kind])) {
    invalid();
    return;
  }
  if (addAttempts.has(getState)) {
    return;
  }
  let data: StateRecord = {};
  try {
    const entity = _.cloneDeep(item[kind]);
    data = kind === 'author' ? getNewAuthor(entity, payload) : getNewBook(entity, payload);
  } catch {
    invalid();
    return;
  }
  const attempt = {};
  addAttempts.set(getState, attempt);
  dispatch(set({ section, isAdding: true }));
  const finish = (error: unknown) => {
    if (addAttempts.get(getState) === attempt) {
      addAttempts.delete(getState);
      dispatch(set({ section, isAdding: false, isAdded: error === null, addError: error }));
    }
  };
  const { request } = requestLegacy({ url: `/${kind}`, method: 'POST', dataType: 'json', contentType: 'application/json', data: JSON.stringify(data) });
  request.done((response) => {
    try {
      if (kind === 'author') {
        const author = parseLibraryAuthor(response);
        if (!author.foreignAuthorId) {
          throw new TypeError('Unexpected author');
        }
        dispatch(updateItem({ ...author, section: 'authors' }));
      } else {
        const book = parseLibraryBook(response);
        if (!book.foreignBookId || !book.author) {
          throw new TypeError('Unexpected book');
        }
        const current = sectionRows(getState, section).find((candidate) => candidate.foreignId === foreignId && isStateRecord(candidate.book));
        dispatch(batchActions([
          updateItem({ ...book.author, section: 'authors' }),
          updateItem({ ...book, section: 'books' }),
          ...(current ? [updateItem({ section, id: current.id, book, updateOnly: true })] : [])
        ]));
      }
      finish(null);
    } catch {
      finish(new ApiError({ kind: 'invalid-response' }));
    }
  });
  request.fail(finish);
}

export const actionHandlers = handleThunks({
  [GET_SEARCH_RESULTS]: (getState, payload, dispatch) => {
    if (typeof payload.term !== 'string' || !payload.term.trim()) {
      reads.cancel(getState, section);
      dispatch(set({ section, isFetching: false, isPopulated: false, items: [], itemMap: {}, error: null }));
      return;
    }
    const attempt = reads.begin(getState, section);
    dispatch(set({ section, isFetching: true }));
    const { request, abortRequest } = requestLegacy({ url: '/search', data: { term: payload.term } });
    attempt.attachAbort(abortRequest);
    request.done((response) => {
      if (!attempt.finish()) {
        return;
      }
      try {
        dispatch(batchActions([update({ section, data: parseSearchResponse(response) }), set({ section, isFetching: false, isPopulated: true, error: null })]));
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
  [CLEAR_SEARCH_RESULTS]: (getState, _payload, dispatch) => {
    reads.cancel(getState, section);
    addAttempts.delete(getState);
    dispatch(clearResultAction());
  },
  [ADD_AUTHOR]: (getState, payload, dispatch) => addResult(getState, payload, dispatch, 'author'),
  [ADD_BOOK]: (getState, payload, dispatch) => addResult(getState, payload, dispatch, 'book')
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_AUTHOR_ADD_DEFAULT]: function(state, { payload }) {
    const newState = getSectionState(state, section);

    newState.authorDefaults = {
      ...asStateRecord(newState.authorDefaults),
      ...asStateRecord(payload)
    };

    return updateSectionState(state, section, newState);
  },

  [SET_BOOK_ADD_DEFAULT]: function(state, { payload }) {
    const newState = getSectionState(state, section);

    newState.bookDefaults = {
      ...asStateRecord(newState.bookDefaults),
      ...asStateRecord(payload)
    };

    return updateSectionState(state, section, newState);
  },

  [CLEAR_SEARCH_RESULTS]: function(state) {
    return { ...state, isFetching: false, isPopulated: false, error: null, isAdding: false, isAdded: false, addError: null, items: [], itemMap: {} };
  }

}, defaultState, section);
