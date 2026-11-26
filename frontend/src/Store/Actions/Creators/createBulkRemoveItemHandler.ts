import { batchActions } from 'redux-batched-actions';
import { isItemId, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { removeItem, set } from '../baseActions';
import { requestLegacy } from './legacyRequest';

function createBulkRemoveItemHandler(section: string, url: string) {
  return function(_getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
    const {
      ids
    } = payload;

    if (!Array.isArray(ids) || !ids.every(isItemId)) {
      dispatch(set({ section, isDeleting: false, deleteError: new ApiError({ kind: 'invalid-response' }) }));
      return;
    }

    dispatch(set({ section, isDeleting: true }));

    const ajaxOptions = {
      url: `${url}`,
      method: 'DELETE',
      data: JSON.stringify(payload),
      dataType: 'json'
    };

    const promise = requestLegacy(ajaxOptions).request;

    promise.done(() => {
      dispatch(batchActions([
        set({
          section,
          isDeleting: false,
          deleteError: null
        }),

        ...ids.map((id) => {
          return removeItem({ section, id });
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isDeleting: false,
        deleteError: xhr
      }));
    });

    return promise;
  };
}

export default createBulkRemoveItemHandler;
