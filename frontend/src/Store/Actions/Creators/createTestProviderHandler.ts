import { asStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import serializeLegacyQuery from 'Utilities/serializeLegacyQuery';
import { set } from '../baseActions';
import { requestLegacy } from './legacyRequest';
import { createProviderSaveData, isAborted } from './providerRequests';
import { createSectionRequests } from './sectionRequests';

const requests = createSectionRequests();

export function createCancelTestProviderHandler(section: string) {
  return function(getState: () => LegacyRootState, _payload: StateRecord, dispatch: LegacyDispatch) {
    if (requests.cancel(getState, section)) {
      dispatch(set({ section, isTesting: false, saveError: null }));
    }
  };
}

function createTestProviderHandler(section: string, url: string) {
  return function(getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
    const { queryParams, id, ...otherPayload } = payload;
    const test = createProviderSaveData(id, otherPayload, getState, section);
    if (!test || Array.isArray(test.data)) {
      requests.cancel(getState, section);
      dispatch(set({ section, isTesting: false, saveError: new ApiError({ kind: 'invalid-request' }) }));
      return;
    }
    const data = test.data;
    const params = { ...asStateRecord(queryParams) };
    const retryData = { url, params: { ...params }, data };
    const attempt = requests.begin(getState, section, retryData);
    if (attempt.retry) {
      params.forceTest = true;
    }
    dispatch(set({ section, isTesting: true }));

    try {
      const { request, abortRequest } = requestLegacy({
        url: `${url}/test?${serializeLegacyQuery(params)}`,
        method: 'POST',
        contentType: 'application/json',
        dataType: 'json',
        data: JSON.stringify(data)
      });
      attempt.attachAbort(abortRequest);

      // Successful provider tests may return an empty/204 response; there is no
      // resource to reconcile or replace in the store.
      request.done(() => {
        if (attempt.finish()) {
          dispatch(set({ section, isTesting: false, saveError: null }));
        }
      });
      request.fail((xhr) => {
        const aborted = isAborted(xhr);
        if (attempt.finish(aborted ? undefined : retryData)) {
          dispatch(set({ section, isTesting: false, saveError: aborted ? null : xhr }));
        }
      });
    } catch {
      if (attempt.finish()) {
        dispatch(set({ section, isTesting: false, saveError: new ApiError({ kind: 'invalid-request' }) }));
      }
    }
  };
}

export default createTestProviderHandler;
