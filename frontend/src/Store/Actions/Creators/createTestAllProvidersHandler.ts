import type { LegacyDispatch, LegacyRootState, StateRecord } from 'Store/StateTypes';
import { set } from '../baseActions';
import { requestLegacy } from './legacyRequest';

function createTestAllProvidersHandler(section: string, url: string) {
  return function(_getState: () => LegacyRootState, _payload: StateRecord, dispatch: LegacyDispatch) {
    dispatch(set({ section, isTestingAll: true }));

    const ajaxOptions = {
      url: `${url}/testall`,
      method: 'POST',
      contentType: 'application/json',
      dataType: 'json'
    };

    const { request } = requestLegacy(ajaxOptions);

    request.done(() => {
      dispatch(set({
        section,
        isTestingAll: false,
        saveError: null
      }));
    });

    request.fail(() => {
      dispatch(set({
        section,
        isTestingAll: false
      }));
    });
  };
}

export default createTestAllProvidersHandler;
