import { batchActions } from 'redux-batched-actions';
import { asStateRecord, isStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getSectionState from 'Utilities/State/getSectionState';
import { set, update } from '../baseActions';
import { requestLegacy } from './legacyRequest';

function createSaveHandler(section: string, url: string) {
  return function(getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
    dispatch(set({ section, isSaving: true }));

    const state = asStateRecord(getSectionState(getState(), section, true));
    const saveData = { ...asStateRecord(state.item), ...asStateRecord(state.pendingChanges), ...payload };

    const promise = requestLegacy({
      url,
      method: 'PUT',
      dataType: 'json',
      data: JSON.stringify(saveData)
    }).request;

    promise.done((data) => {
      if (!isStateRecord(data)) {
        dispatch(set({ section, isSaving: false, saveError: new ApiError({ kind: 'invalid-response' }) }));
        return;
      }

      dispatch(batchActions([
        update({ section, data }),

        set({
          section,
          isSaving: false,
          saveError: null,
          pendingChanges: {}
        })
      ]));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSaving: false,
        saveError: xhr
      }));
    });
  };
}

export default createSaveHandler;
