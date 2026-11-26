import { batchActions } from 'redux-batched-actions';
import { isItemId, isStateRecord, type ItemId, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { set, updateItem } from '../baseActions';
import { requestLegacy } from './legacyRequest';

function createBulkEditItemHandler(section: string, url: string) {
  return function(_getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {

    dispatch(set({ section, isSaving: true }));

    const ajaxOptions = {
      url: `${url}`,
      method: 'PUT',
      data: JSON.stringify(payload),
      dataType: 'json'
    };

    const promise = requestLegacy(ajaxOptions).request;

    promise.done((data) => {
      if (!Array.isArray(data) || !data.every((item: unknown): item is StateRecord & { id: ItemId } => isStateRecord(item) && isItemId(item.id))) {
        dispatch(set({ section, isSaving: false, saveError: new ApiError({ kind: 'invalid-response' }) }));
        return;
      }

      dispatch(batchActions([
        set({
          section,
          isSaving: false,
          saveError: null
        }),

        ...data.map((provider) => updateItem({ ...provider, section, id: provider.id }))
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
      }));
    });

    return promise;
  };
}

export default createBulkEditItemHandler;
