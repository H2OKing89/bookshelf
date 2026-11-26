import moment from 'moment';
import React from 'react';
import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { parseLibraryBook, parseLibraryCollection } from 'Book/LibraryModels';
import Icon from 'Components/Icon';
import { filterTypes, icons, sortDirections } from 'Helpers/Props';
import { asStateRecord, isStateRecord, type LegacyDispatch,type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import dateFilterPredicate from 'Utilities/Date/dateFilterPredicate';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import translate from 'Utilities/String/translate';
import { removeItem, set, update, updateItem } from './baseActions';
import createHandleActions from './Creators/createHandleActions';
import createRemoveItemHandler from './Creators/createRemoveItemHandler';
import createSaveProviderHandler from './Creators/createSaveProviderHandler';
import { requestLegacy } from './Creators/legacyRequest';
import { bookSection, dateValue, isLibraryId, libraryIds, matchesFilter, numberValue, parseExactRows,sectionRows, textValue } from './Creators/libraryActionsState';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetSettingValueReducer from './Creators/Reducers/createSetSettingValueReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';
import { createSectionRequests } from './Creators/sectionRequests';
import { createSettingValueAction } from './Settings/settingActions';

//
// Variables

export const section = 'books';

export const filters = [
  {
    key: 'all',
    label: () => translate('All'),
    filters: []
  },
  {
    key: 'monitored',
    label: () => translate('Monitored'),
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
    label: () => translate('Unmonitored'),
    filters: [
      {
        key: 'monitored',
        value: false,
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'missing',
    label: () => translate('Missing'),
    filters: [
      {
        key: 'monitored',
        value: true,
        type: filterTypes.EQUAL
      },
      {
        key: 'missing',
        value: true,
        type: filterTypes.EQUAL
      }
    ]
  },
  {
    key: 'wanted',
    label: () => translate('Wanted'),
    filters: [
      {
        key: 'monitored',
        value: true,
        type: filterTypes.EQUAL
      },
      {
        key: 'missing',
        value: true,
        type: filterTypes.EQUAL
      },
      {
        key: 'releaseDate',
        value: moment(),
        type: filterTypes.LESS_THAN
      }
    ]
  }
];

export const filterPredicates = {
  title(item: StateRecord, filterValue: unknown, type: string) {
    const titles = [item.title, item.displayTitle || item.title];
    const negative = type === filterTypes.NOT_CONTAINS || type === filterTypes.NOT_EQUAL ||
      type === filterTypes.NOT_STARTS_WITH || type === filterTypes.NOT_ENDS_WITH;
    return negative ? titles.every((title) => matchesFilter(type, title, filterValue)) : titles.some((title) => matchesFilter(type, title, filterValue));
  },
  metadataLabels(item: StateRecord, filterValue: unknown, type: string) {
    return matchesFilter(type, item.metadataLabels ?? [], filterValue);
  },
  missing(item: StateRecord) {
    return numberValue(asStateRecord(item.statistics).bookFileCount) === 0;
  },
  releaseDate(item: StateRecord, filterValue: unknown, type: string) {
    return dateFilterPredicate(dateValue(item.releaseDate), filterValue, type);
  },
  added(item: StateRecord, filterValue: unknown, type: string) {
    return dateFilterPredicate(dateValue(item.added), filterValue, type);
  },
  qualityProfileId(item: StateRecord, filterValue: unknown, type: string) {
    return isStateRecord(item.author) && matchesFilter(type, item.author.qualityProfileId, filterValue);
  },
  ratings(item: StateRecord, filterValue: unknown, type: string) {
    return matchesFilter(type, numberValue(asStateRecord(item.ratings).value) * 10, filterValue);
  },
  path(item: StateRecord, filterValue: unknown, type: string) {
    return isStateRecord(item.author) && matchesFilter(type, item.author.path, filterValue);
  },
  bookFileCount(item: StateRecord, filterValue: unknown, type: string) {
    return matchesFilter(type, numberValue(asStateRecord(item.statistics).bookFileCount), filterValue);
  },
  sizeOnDisk(item: StateRecord, filterValue: unknown, type: string) {
    return matchesFilter(type, numberValue(asStateRecord(item.statistics).sizeOnDisk), filterValue);
  }
};

export const sortPredicates = {
  fileQualityRank(item: StateRecord) {
    return numberValue(asStateRecord(item.statistics).fileQualityRank, -2);
  },
  title(item: StateRecord) {
    return textValue(item.displayTitle) || textValue(item.title);
  },
  sizeOnDisk(item: StateRecord) {
    return numberValue(asStateRecord(item.statistics).sizeOnDisk);
  },
  path(item: StateRecord) {
    return asStateRecord(item.author).path;
  },
  series(item: StateRecord) {
    return item.displaySeriesTitle === undefined ? item.seriesTitle : item.displaySeriesTitle;
  },
  rating(item: StateRecord) {
    return numberValue(asStateRecord(item.ratings).value);
  },
  status(item: StateRecord) {
    const hasFile = numberValue(asStateRecord(item.statistics).bookFileCount) > 0;
    const available = typeof item.releaseDate === 'string' && Date.parse(item.releaseDate) < Date.now();
    return (available ? 1 : 0) + (item.monitored === true ? 2 : 0) + (hasFile ? 4 : 0);
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
  sortKey: 'releaseDate',
  sortDirection: sortDirections.DESCENDING,
  items: [],
  pendingChanges: {},
  sortPredicates: {
    rating: sortPredicates.rating
  },

  columns: [
    {
      name: 'select',
      columnLabel: 'Select',
      isSortable: false,
      isVisible: true,
      isModifiable: false,
      isHidden: true
    },
    {
      name: 'monitored',
      columnLabel: 'Monitored',
      isVisible: true,
      isModifiable: false
    },
    {
      name: 'title',
      label: 'Title',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'series',
      label: 'Series',
      isSortable: true,
      isVisible: false
    },
    {
      name: 'releaseDate',
      label: 'Release Date',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'runtimeLengthMinutes',
      label: 'Runtime',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'narrators',
      label: 'Narrators',
      isSortable: false,
      isVisible: true
    },
    {
      name: 'pageCount',
      label: 'Pages',
      isSortable: true,
      isVisible: false
    },
    {
      name: 'rating',
      label: 'Rating',
      isSortable: true,
      isVisible: true
    },
    {
      name: 'indexerFlags',
      columnLabel: () => translate('IndexerFlags'),
      label: React.createElement(Icon, {
        name: icons.FLAG,
        title: () => translate('IndexerFlags')
      }),
      isVisible: false
    },
    {
      name: 'status',
      label: 'Status',
      isVisible: true,
      isSortable: true
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
  'books.sortKey',
  'books.sortDirection',
  'books.columns'
];

//
// Actions Types

export const FETCH_BOOKS = 'books/fetchBooks';
export const SET_BOOKS_SORT = 'books/setBooksSort';
export const SET_BOOKS_TABLE_OPTION = 'books/setBooksTableOption';
export const CLEAR_BOOKS = 'books/clearBooks';
export const SET_BOOK_VALUE = 'books/setBookValue';
export const SAVE_BOOK = 'books/saveBook';
export const DELETE_BOOK = 'books/deleteBook';
export const DELETE_AUTHOR_BOOKS = 'books/deleteAuthorBooks';
export const TOGGLE_BOOK_MONITORED = 'books/toggleBookMonitored';
export const TOGGLE_BOOKS_MONITORED = 'books/toggleBooksMonitored';

//
// Action Creators

export const fetchBooks = createThunk(FETCH_BOOKS);
export const setBooksSort = createAction<{ sortKey?: string; sortDirection?: 'ascending' | 'descending' }>(SET_BOOKS_SORT);
export const setBooksTableOption = createAction<StateRecord>(SET_BOOKS_TABLE_OPTION);
export const clearBooks = createAction(CLEAR_BOOKS);
export const toggleBookMonitored = createThunk(TOGGLE_BOOK_MONITORED);
export const toggleBooksMonitored = createThunk(TOGGLE_BOOKS_MONITORED);

export const saveBook = createThunk(SAVE_BOOK);

export const deleteBook = createThunk(DELETE_BOOK, (payload) => {
  return {
    ...payload,
    queryParams: {
      deleteFiles: payload.deleteFiles,
      addImportListExclusion: payload.addImportListExclusion
    }
  };
});

export const deleteAuthorBooks = createThunk(DELETE_AUTHOR_BOOKS, (payload) => {
  return {
    ...payload,
    queryParams: {
      authorId: payload.authorId
    }
  };
});

export const setBookValue = createSettingValueAction(SET_BOOK_VALUE, section);

const reads = createSectionRequests();
const monitorAttempts = new WeakMap<() => LegacyRootState, Map<string, object>>();

function monitorBooks(getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
  const ids = libraryIds(payload.bookIds);
  const monitored = payload.monitored;
  const target = bookSection(payload.bookEntity);
  if (!ids || typeof monitored !== 'boolean' || target === null) {
    return;
  }
  let latest = monitorAttempts.get(getState);
  if (!latest) {
    latest = new Map();
    monitorAttempts.set(getState, latest);
  }
  const attempts = latest;
  const started = {};
  const key = (id: number) => `${target}:${id}`;
  ids.forEach((id) => attempts.set(key(id), started));
  const patch = (id: number, fields: StateRecord) => updateItem({ ...fields, section: target, id, updateOnly: true });
  dispatch(batchActions(ids.map((id) => patch(id, { isSaving: true }))));
  const { request } = requestLegacy({ url: '/book/monitor', method: 'PUT', data: JSON.stringify({ bookIds: ids, monitored }), dataType: 'json' });
  const finish = (rows?: { id: number; monitored?: boolean | null }[]) => {
    const current = ids.filter((id) => attempts.get(key(id)) === started);
    current.forEach((id) => attempts.delete(key(id)));
    if (current.length) {
      dispatch(batchActions(current.map((id) => {
        const row = rows?.find((item) => item.id === id);
        return patch(id, { isSaving: false, ...(typeof row?.monitored === 'boolean' && { monitored: row.monitored }) });
      })));
    }
  };
  request.done((data) => {
    try {
      finish(parseExactRows(data, ids, parseLibraryBook));
    } catch {
      finish();
    }
  });
  request.fail(() => finish());
}

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_BOOKS]: (getState, payload, dispatch) => {
    const authorId = payload.authorId;
    if (authorId != null && !isLibraryId(authorId)) {
      dispatch(set({ section, isFetching: false, isPopulated: false, error: new ApiError({ kind: 'invalid-request' }) }));
      return;
    }
    const key = authorId == null ? section : `${section}:${authorId}`;
    const attempt = reads.begin(getState, key);
    dispatch(set({ section, isFetching: true }));
    const { request, abortRequest } = requestLegacy({ url: '/book', data: payload, traditional: true });
    attempt.attachAbort(abortRequest);
    request.done((response) => {
      if (!attempt.finish()) {
        return;
      }
      try {
        const items = parseLibraryCollection(response, parseLibraryBook);
        if (authorId != null && items.some((item) => item.authorId !== authorId)) {
          throw new TypeError('Invalid author collection');
        }
        // Keep every unrelated author in the current store, not its request-time snapshot.
        const data = authorId == null ? items : [...sectionRows(getState, section).filter((item) => item.authorId !== authorId), ...items];
        dispatch(batchActions([update({ section, data }), set({ section, isFetching: false, isPopulated: true, error: null })]));
      } catch {
        dispatch(set({ section, isFetching: false, isPopulated: false, error: new ApiError({ kind: 'invalid-response' }) }));
      }
    });
    request.fail((xhr) => {
      if (attempt.finish()) {
        dispatch(set({ section, isFetching: false, isPopulated: false, error: isStateRecord(xhr) && xhr.aborted === true ? null : xhr }));
      }
    });
    return abortRequest;
  },

  [SAVE_BOOK]: createSaveProviderHandler(section, '/book'),
  [DELETE_BOOK]: createRemoveItemHandler(section, '/book'),

  [DELETE_AUTHOR_BOOKS]: (getState, payload, dispatch) => {
    const { authorId } = payload;
    if (isLibraryId(authorId)) {
      const books = sectionRows(getState, section).filter((book) => book.authorId === authorId);
      dispatch(batchActions(books.map((book) => removeItem({ section, id: book.id }))));
    }
  },

  [TOGGLE_BOOK_MONITORED]: (getState, payload, dispatch) => monitorBooks(getState, { ...payload, bookIds: [payload.bookId] }, dispatch),
  [TOGGLE_BOOKS_MONITORED]: monitorBooks
});

//
// Reducers

export const reducers = createHandleActions({

  [SET_BOOKS_SORT]: createSetClientSideCollectionSortReducer(section),

  [SET_BOOKS_TABLE_OPTION]: createSetTableOptionReducer(section),

  [SET_BOOK_VALUE]: createSetSettingValueReducer(section),

  [CLEAR_BOOKS]: (state) => {
    return Object.assign({}, state, {
      isFetching: false,
      isPopulated: false,
      error: null,
      items: [],
      itemMap: {}
    });
  }

}, defaultState, section);
