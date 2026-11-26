import _ from 'lodash';
import { batchActions } from 'redux-batched-actions';
import { parseHistoryRecord, scheduleHistoryRefresh } from 'Activity/History/historyPageQuery';
import { isStateRecord, type LegacyRootState, type LegacyThunk, type StateRecord, type StoreAction } from 'Store/StateTypes';
import type { ThunkHandler } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { set, update } from '../baseActions';
import { requestLegacy } from './legacyRequest';
import { positiveInteger } from './pagedCollection';
import { createSectionRequests } from './sectionRequests';

interface DetailHistoryOptions {
  section: string;
  path: string;
  query: (payload: StateRecord) => StateRecord | null;
  parse: (value: unknown) => unknown;
  refetch: (payload: StateRecord) => LegacyThunk;
  clear: () => StoreAction;
}

export function parseDetailHistory(value: unknown) {
  if (!Array.isArray(value)) {
    throw new TypeError('Invalid history collection');
  }
  const records = value.map((record: unknown) => {
    if (!isStateRecord(record)) {
      throw new TypeError('Invalid history record');
    }
    return { ...record, ...parseHistoryRecord(record) };
  });
  if (new Set(records.map((record) => record.id)).size !== records.length) {
    throw new TypeError('Duplicate history identity');
  }
  return records;
}

export function createDetailHistoryRequests(options: DetailHistoryOptions) {
  const { section } = options;
  const requests = createSectionRequests();
  const queries = new WeakMap<() => LegacyRootState, StateRecord>();

  const fetch: ThunkHandler = (getState, payload, dispatch) => {
    const query = options.query(payload);
    if (!query) {
      queries.delete(getState);
      requests.cancel(getState, section);
      dispatch(set({ section, isFetching: false, isPopulated: false, error: new ApiError({ kind: 'invalid-request' }) }));
      return;
    }

    const previous = queries.get(getState);
    queries.set(getState, query);
    const attempt = requests.begin(getState, section);
    dispatch(set({ section, isFetching: true, ...(!_.isEqual(previous, query) && { items: [], itemMap: {}, isPopulated: false }) }));
    const { request, abortRequest } = requestLegacy({ url: options.path, data: query });
    attempt.attachAbort(abortRequest);
    request.done((data) => {
      if (!attempt.finish()) {
        return;
      }
      try {
        const records = options.parse(data);
        dispatch(batchActions([
          update({ section, data: records }),
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
    return () => {
      if (attempt.finish()) {
        abortRequest();
        dispatch(set({ section, isFetching: false }));
      }
    };
  };

  const clear: ThunkHandler = (getState, _payload, dispatch) => {
    queries.delete(getState);
    requests.cancel(getState, section);
    dispatch(options.clear());
  };

  const markFailed: ThunkHandler = (getState, payload, dispatch) => {
    const { historyId } = payload;
    const query = options.query(payload);
    if (!positiveInteger(historyId) || !query) {
      return;
    }
    const { request } = requestLegacy({ url: `/history/failed/${historyId}`, method: 'POST' });
    request.done(() => {
      scheduleHistoryRefresh();
      // A mutation may complete after this detail page has been dismissed or
      // reused for a different book; never fetch an obsolete selection.
      if (_.isEqual(queries.get(getState), query)) {
        dispatch(options.refetch(payload));
      }
    });
  };

  return { fetch, clear, markFailed };
}
