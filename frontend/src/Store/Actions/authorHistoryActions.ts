import { createAction } from 'redux-actions';
import { createThunk, handleThunks } from 'Store/thunks';
import createHandleActions from './Creators/createHandleActions';
import { createDetailHistoryRequests, parseDetailHistory } from './Creators/detailHistoryRequests';
import { positiveInteger } from './Creators/pagedCollection';
import createClearReducer from './Creators/Reducers/createClearReducer';

export const section = 'authorHistory';
export const defaultState = { isFetching: false, isPopulated: false, error: null, items: [], itemMap: {} };

export const FETCH_AUTHOR_HISTORY = 'authorHistory/fetchAuthorHistory';
export const CLEAR_AUTHOR_HISTORY = 'authorHistory/clearAuthorHistory';
export const AUTHOR_HISTORY_MARK_AS_FAILED = 'authorHistory/authorHistoryMarkAsFailed';

export const fetchAuthorHistory = createThunk(FETCH_AUTHOR_HISTORY);
export const clearAuthorHistory = createThunk(CLEAR_AUTHOR_HISTORY);
export const authorHistoryMarkAsFailed = createThunk(AUTHOR_HISTORY_MARK_AS_FAILED);

const requests = createDetailHistoryRequests({
  section,
  path: '/history/author',
  query: (payload) => {
    const { authorId, bookId, ...rest } = payload;
    if (!positiveInteger(authorId) || (bookId != null && !positiveInteger(bookId))) {
      return null;
    }
    delete rest.historyId;
    return { ...rest, authorId, ...(bookId == null ? {} : { bookId }) };
  },
  parse: parseDetailHistory,
  refetch: fetchAuthorHistory,
  clear: createAction(CLEAR_AUTHOR_HISTORY)
});

export const actionHandlers = handleThunks({
  [FETCH_AUTHOR_HISTORY]: requests.fetch,
  [CLEAR_AUTHOR_HISTORY]: requests.clear,
  [AUTHOR_HISTORY_MARK_AS_FAILED]: requests.markFailed
});

export const reducers = createHandleActions({
  [CLEAR_AUTHOR_HISTORY]: createClearReducer(section, defaultState)
}, defaultState, section);
