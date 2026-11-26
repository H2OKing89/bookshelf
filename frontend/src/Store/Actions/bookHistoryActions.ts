import { createAction } from 'redux-actions';
import { isStateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import createHandleActions from './Creators/createHandleActions';
import { createDetailHistoryRequests, parseDetailHistory } from './Creators/detailHistoryRequests';
import { positiveInteger } from './Creators/pagedCollection';
import createClearReducer from './Creators/Reducers/createClearReducer';

export const section = 'bookHistory';
export const defaultState = { isFetching: false, isPopulated: false, error: null, items: [], itemMap: {} };

export const FETCH_BOOK_HISTORY = 'bookHistory/fetchBookHistory';
export const CLEAR_BOOK_HISTORY = 'bookHistory/clearBookHistory';
export const BOOK_HISTORY_MARK_AS_FAILED = 'bookHistory/bookHistoryMarkAsFailed';

export const fetchBookHistory = createThunk(FETCH_BOOK_HISTORY);
export const clearBookHistory = createThunk(CLEAR_BOOK_HISTORY);
export const bookHistoryMarkAsFailed = createThunk(BOOK_HISTORY_MARK_AS_FAILED);

const requests = createDetailHistoryRequests({
  section,
  path: '/history',
  query: (payload) => {
    const bookId = positiveInteger(payload.bookId);
    return bookId ? { pageSize: 1000, page: 1, sortKey: 'date', sortDirection: 'descending', bookId } : null;
  },
  parse: (value) => {
    if (!isStateRecord(value)) {
      throw new TypeError('Invalid history page');
    }
    return parseDetailHistory(value.records);
  },
  refetch: fetchBookHistory,
  clear: createAction(CLEAR_BOOK_HISTORY)
});

export const actionHandlers = handleThunks({
  [FETCH_BOOK_HISTORY]: requests.fetch,
  [CLEAR_BOOK_HISTORY]: requests.clear,
  [BOOK_HISTORY_MARK_AS_FAILED]: requests.markFailed
});

export const reducers = createHandleActions({
  [CLEAR_BOOK_HISTORY]: createClearReducer(section, defaultState)
}, defaultState, section);
