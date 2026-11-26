import { batchActions } from 'redux-batched-actions';
import { libraryResponseParser, parseLibraryAuthor } from 'Book/LibraryModels';
import { filterTypes, sortDirections } from 'Helpers/Props';
import { asStateRecord, isStateRecord, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import dateFilterPredicate from 'Utilities/Date/dateFilterPredicate';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { set, updateItem } from './baseActions';
import { fetchBooks } from './bookActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createRemoveItemHandler from './Creators/createRemoveItemHandler';
import createSaveProviderHandler from './Creators/createSaveProviderHandler';
import { requestLegacy } from './Creators/legacyRequest';
import { dateValue, isLibraryId, matchesFilter, numberValue, sectionRows } from './Creators/libraryActionsState';
import createSetSettingValueReducer from './Creators/Reducers/createSetSettingValueReducer';
import { createSettingValueAction } from './Settings/settingActions';

//
// Variables

export const section = 'authors';

export const filters = [
  {
    key: 'all',
    label: 'All',
    filters: []
  },
  {
    key: 'monitored',
    label: 'Monitored Only',
    filters: [
      {
        key: 'monitored',
        value: true,
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'unmonitored',
    label: 'Unmonitored Only',
    filters: [
      {
        key: 'monitored',
        value: false,
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'continuing',
    label: 'Continuing Only',
    filters: [
      {
        key: 'status',
        value: 'continuing',
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'ended',
    label: 'Ended Only',
    filters: [
      {
        key: 'status',
        value: 'ended',
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'missing',
    label: 'Missing Books',
    filters: [
      {
        key: 'missing',
        value: true,
        type: filterTypes.EQUAL
      }
    ]
  }
];

export const filterPredicates = {
  missing(item: StateRecord) {
    const stats = asStateRecord(item.statistics);
    return numberValue(stats.bookCount) - numberValue(stats.bookFileCount) > 0;
  },
  nextBook(item: StateRecord, filterValue: unknown, type: string) {
    return dateFilterPredicate(dateValue(asStateRecord(item.nextBook).releaseDate ?? item.nextBook), filterValue, type);
  },
  lastBook(item: StateRecord, filterValue: unknown, type: string) {
    return dateFilterPredicate(dateValue(asStateRecord(item.lastBook).releaseDate ?? item.lastBook), filterValue, type);
  },
  added(item: StateRecord, filterValue: unknown, type: string) {
    return dateFilterPredicate(dateValue(item.added), filterValue, type);
  },
  ratings(item: StateRecord, filterValue: unknown, type: string) {
    return matchesFilter(type, numberValue(asStateRecord(item.ratings).value) * 10, filterValue);
  },
  bookCount(item: StateRecord, filterValue: unknown, type: string) {
    return matchesFilter(type, numberValue(asStateRecord(item.statistics).bookCount), filterValue);
  },
  sizeOnDisk(item: StateRecord, filterValue: unknown, type: string) {
    return matchesFilter(type, numberValue(asStateRecord(item.statistics).sizeOnDisk), filterValue);
  }
};

export const sortPredicates = {
  status(item: StateRecord) {
    return (item.monitored === true ? 2 : 0) + (item.status === 'continuing' ? 1 : 0);
  },
  sizeOnDisk(item: StateRecord) {
    return numberValue(asStateRecord(item.statistics).sizeOnDisk);
  }
};

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  isSaving: false,
  saveError: null,
  items: [],
  sortKey: 'sortName',
  sortDirection: sortDirections.ASCENDING,
  pendingChanges: {}
};

//
// Actions Types

export const FETCH_AUTHOR = 'authors/fetchAuthor';
export const SET_AUTHOR_VALUE = 'authors/setAuthorValue';
export const SAVE_AUTHOR = 'authors/saveAuthor';
export const DELETE_AUTHOR = 'authors/deleteAuthor';

export const TOGGLE_AUTHOR_MONITORED = 'authors/toggleAuthorMonitored';
export const TOGGLE_BOOK_MONITORED = 'authors/toggleBookMonitored';
export const UPDATE_BOOK_MONITORED = 'authors/updateBookMonitored';

//
// Action Creators

export const fetchAuthor = createThunk(FETCH_AUTHOR);
export const saveAuthor = createThunk(SAVE_AUTHOR, (payload) => {
  const newPayload = {
    ...payload
  };

  if (payload.moveFiles) {
    newPayload.queryParams = {
      moveFiles: true
    };
  }

  delete newPayload.moveFiles;

  return newPayload;
});

export const deleteAuthor = createThunk(DELETE_AUTHOR, (payload) => {
  return {
    ...payload,
    queryParams: {
      deleteFiles: payload.deleteFiles,
      addImportListExclusion: payload.addImportListExclusion
    }
  };
});

export const toggleAuthorMonitored = createThunk(TOGGLE_AUTHOR_MONITORED);
export const toggleBookMonitored = createThunk(TOGGLE_BOOK_MONITORED);
export const updateBookMonitor = createThunk(UPDATE_BOOK_MONITORED);

export const setAuthorValue = createSettingValueAction(SET_AUTHOR_VALUE, section);

//
// Action Handlers

export const actionHandlers = handleThunks({

  [FETCH_AUTHOR]: createFetchHandler(section, '/author', libraryResponseParser(parseLibraryAuthor)),
  [SAVE_AUTHOR]: createSaveProviderHandler(section, '/author'),
  [DELETE_AUTHOR]: createRemoveItemHandler(section, '/author'),

  [TOGGLE_AUTHOR_MONITORED]: (getState, payload, dispatch) => {
    const { authorId: id, monitored } = payload;
    if (!isLibraryId(id) || typeof monitored !== 'boolean') {
      return;
    }
    const author = sectionRows(getState, section).find((item) => item.id === id);
    if (!author || author.isSaving === true) {
      return;
    }
    dispatch(updateItem({ id, section, isSaving: true, updateOnly: true }));
    const { request } = requestLegacy({ url: `/author/${id}`, method: 'PUT', data: JSON.stringify({ ...author, monitored }), dataType: 'json' });
    request.done((data) => {
      try {
        const item = parseLibraryAuthor(data);
        if (item.id !== id || typeof item.monitored !== 'boolean') {
          throw new TypeError('Invalid author response');
        }
        dispatch(updateItem({ id, section, isSaving: false, monitored: item.monitored, updateOnly: true }));
      } catch {
        dispatch(updateItem({ id, section, isSaving: false, updateOnly: true }));
      }
    });
    request.fail(() => dispatch(updateItem({ id, section, isSaving: false, updateOnly: true })));
  },

  [TOGGLE_BOOK_MONITORED]: (getState, payload, dispatch) => {
    const { authorId: id, seasonNumber, monitored } = payload;
    if (!isLibraryId(id) || typeof seasonNumber !== 'number' || !Number.isFinite(seasonNumber) || typeof monitored !== 'boolean') {
      return;
    }
    const author = sectionRows(getState, section).find((item) => item.id === id);
    if (!author || !Array.isArray(author.seasons)) {
      return;
    }
    const original: unknown[] = author.seasons;
    const season = original.find((item) => isStateRecord(item) && item.seasonNumber === seasonNumber);
    if (!isStateRecord(season) || season.isSaving === true) {
      return;
    }
    const pending = original.map((item) => item === season ? { ...season, isSaving: true } : item);
    dispatch(updateItem({ id, section, seasons: pending, updateOnly: true }));
    const seasons = original.map((item) => item === season ? { ...season, monitored } : item);
    const { request } = requestLegacy({ url: `/author/${id}`, method: 'PUT', data: JSON.stringify({ ...author, seasons }), dataType: 'json' });
    const fail = () => dispatch(updateItem({ id, section, seasons: original, updateOnly: true }));
    request.done((data) => {
      try {
        const saved = parseLibraryAuthor(data);
        if (saved.id !== id) {
          throw new TypeError('Invalid author identity');
        }
        const books = sectionRows(getState, 'books').filter((book) => book.authorId === id && book.seasonNumber === seasonNumber);
        dispatch(batchActions([
          updateItem({ ...saved, id, section, updateOnly: true }),
          ...books.map((book) => updateItem({ id: book.id, section: 'books', monitored, updateOnly: true }))
        ]));
      } catch {
        fail();
      }
    });
    request.fail(fail);
  },

  [UPDATE_BOOK_MONITORED]: (_getState, payload, dispatch) => {
    const { id, monitor } = payload;
    if (!isLibraryId(id) || typeof monitor !== 'string') {
      dispatch(set({ section, isSaving: false, saveError: new ApiError({ kind: 'invalid-request' }) }));
      return;
    }
    dispatch(set({ section, isSaving: true }));
    const { request } = requestLegacy({
      url: '/bookshelf', method: 'POST',
      data: JSON.stringify({ authors: [{ id }], monitoringOptions: { monitor } }),
      dataType: 'json'
    });
    request.done(() => {
      dispatch(fetchBooks({ authorId: id }));
      dispatch(set({ section, isSaving: false, saveError: null }));
    });
    request.fail((xhr) => dispatch(set({ section, isSaving: false, saveError: xhr })));
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_AUTHOR_VALUE]: createSetSettingValueReducer(section)

}, defaultState, section);
