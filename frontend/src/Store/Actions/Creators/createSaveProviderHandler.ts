import _ from 'lodash';
import { batchActions } from 'redux-batched-actions';
import { announceSettingsSaved } from 'Components/ActionStatus';
import { asStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import serializeLegacyQuery from 'Utilities/serializeLegacyQuery';
import { removeItem, set, updateItem } from '../baseActions';
import { requestLegacy } from './legacyRequest';
import { createProviderSaveData, getDraft, isAborted, parseProviderSaveResponse, remainingDraft } from './providerRequests';
import { createSectionRequests } from './sectionRequests';

const requests = createSectionRequests();

export function createCancelSaveProviderHandler(section: string) {
  return function(getState: () => LegacyRootState, _payload: StateRecord, dispatch: LegacyDispatch) {
    if (requests.cancel(getState, section)) {
      dispatch(set({ section, isSaving: false, saveError: null }));
    }
  };
}

function createSaveProviderHandler(section: string, url: string, removeStale = false) {
  return function(getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
    const { id, queryParams, ...otherPayload } = payload;
    const submitted = _.cloneDeep(getDraft(getState, section));
    const save = createProviderSaveData(id, otherPayload, getState, section);
    if (!save) {
      requests.cancel(getState, section);
      dispatch(set({ section, isSaving: false, saveError: new ApiError({ kind: 'invalid-request' }) }));
      return;
    }

    const { ids, data, method } = save;
    const bulk = Array.isArray(id);
    const requestUrl = method === 'PUT' ? `${url}/${ids.join(',')}` : url;
    const params = { ...asStateRecord(queryParams) };
    const retryData = { requestUrl, params: { ...params }, method, data };
    const attempt = requests.begin(getState, section, retryData);
    if (attempt.retry) {
      params.forceSave = true;
    }

    dispatch(set({ section, isSaving: true }));

    try {
      const { request, abortRequest } = requestLegacy({
        url: `${requestUrl}?${serializeLegacyQuery(params)}`,
        method,
        contentType: 'application/json',
        dataType: 'json',
        data: JSON.stringify(data)
      });
      attempt.attachAbort(abortRequest);

      request.done((response) => {
        const items = parseProviderSaveResponse(response, ids, removeStale, bulk);
        if (!items) {
          if (attempt.finish()) {
            dispatch(set({ section, isSaving: false, saveError: new ApiError({ kind: 'invalid-response' }) }));
          }
          return;
        }
        if (!attempt.finish()) {
          return;
        }
        const toRemove = removeStale && bulk ? ids.filter((item) => !items.some((provider) => String(provider.id) === String(item))) : [];
        const pendingChanges = remainingDraft(submitted, getDraft(getState, section));
        dispatch(batchActions([
          ...items.map((item) => updateItem({ ...item, section, id: item.id })),
          ...toRemove.map((item) => removeItem({ section, id: item })),
          set({ section, isSaving: false, saveError: null, pendingChanges })
        ]));
        announceSettingsSaved();
      });

      request.fail((xhr) => {
        const aborted = isAborted(xhr);
        if (attempt.finish(aborted ? undefined : retryData)) {
          dispatch(set({ section, isSaving: false, saveError: aborted ? null : xhr }));
        }
      });
    } catch {
      if (attempt.finish()) {
        dispatch(set({ section, isSaving: false, saveError: new ApiError({ kind: 'invalid-request' }) }));
      }
    }
  };
}

export default createSaveProviderHandler;
