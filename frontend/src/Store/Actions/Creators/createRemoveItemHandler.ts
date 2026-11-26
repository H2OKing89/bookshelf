import { batchActions } from 'redux-batched-actions';
import { isItemId, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import serializeLegacyQuery from 'Utilities/serializeLegacyQuery';
import { removeItem, set } from '../baseActions';
import { requestLegacy } from './legacyRequest';

function createRemoveItemHandler(section: string, url: string) {
  return function(_getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
    const {
      id,
      ...queryParams
    } = payload;

    if (!isItemId(id)) {
      dispatch(set({ section, isDeleting: false, deleteError: new ApiError({ kind: 'invalid-response' }) }));
      return;
    }

    dispatch(set({ section, isDeleting: true }));

    const ajaxOptions = {
      url: `${url}/${id}?${serializeLegacyQuery(queryParams)}`,
      method: 'DELETE'
    };

    const promise = requestLegacy(ajaxOptions).request;

    promise.done(() => {
      dispatch(batchActions([
        set({
          section,
          isDeleting: false,
          deleteError: null
        }),

        removeItem({ section, id })
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

export default createRemoveItemHandler;
