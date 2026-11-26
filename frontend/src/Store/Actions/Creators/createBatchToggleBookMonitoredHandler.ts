import { batchActions } from 'redux-batched-actions';
import type { LegacyDispatch, LegacyRootState, StateRecord } from 'Store/StateTypes';
import { updateItem } from '../baseActions';
import { requestLegacy } from './legacyRequest';
import type { CollectionFetchThunk } from './pagedCollection';

interface BatchState {
  latest: Map<number, object>;
  inFlight: number;
  refreshNeeded: boolean;
}

function createBatchToggleBookMonitoredHandler(section: string, fetchHandler: CollectionFetchThunk) {
  const stores = new WeakMap<() => LegacyRootState, BatchState>();
  return function(getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
    const { bookIds, monitored } = payload;
    if (!Array.isArray(bookIds) || !bookIds.length || !bookIds.every((id: unknown): id is number =>
      typeof id === 'number' && Number.isSafeInteger(id) && id > 0) || typeof monitored !== 'boolean') {
      return;
    }
    const ids = [...new Set<number>(bookIds)];
    let batch = stores.get(getState);
    if (!batch) {
      batch = { latest: new Map(), inFlight: 0, refreshNeeded: false };
      stores.set(getState, batch);
    }
    const current = batch;
    const attempt = {};
    current.inFlight++;
    ids.forEach((id) => current.latest.set(id, attempt));
    dispatch(batchActions(ids.map((id) => updateItem({ section, id, updateOnly: true, isSaving: true }))));

    let completed = false;
    function finish(success: boolean) {
      if (completed) {
        return;
      }
      completed = true;
      const latest = ids.filter((id) => current.latest.get(id) === attempt);
      latest.forEach((id) => current.latest.delete(id));
      if (latest.length) {
        // Patch current records instead of replacing a stale collection
        // snapshot. A record removed since the request stays removed.
        dispatch(batchActions(latest.map((id) => updateItem({ section, id, updateOnly: true, isSaving: false, ...(success ? { monitored } : {}) }))));
      }
      current.refreshNeeded ||= success;
      current.inFlight--;
      if (current.inFlight === 0) {
        stores.delete(getState);
        if (current.refreshNeeded) {
          dispatch(fetchHandler());
        }
      }
    }

    try {
      const { request } = requestLegacy({ url: '/book/monitor', method: 'PUT', data: JSON.stringify({ bookIds: ids, monitored }), dataType: 'json' });
      request.done(() => finish(true));
      request.fail(() => finish(false));
    } catch {
      finish(false);
    }
  };
}

export default createBatchToggleBookMonitoredHandler;
