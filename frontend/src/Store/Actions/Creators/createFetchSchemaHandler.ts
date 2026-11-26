import { isStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import { set } from '../baseActions';
import { requestLegacy } from './legacyRequest';

function createFetchSchemaHandler(section: string, url: string) {
  return function(_getState: () => LegacyRootState, _payload: StateRecord, dispatch: LegacyDispatch) {
    dispatch(set({ section, isSchemaFetching: true }));

    const promise = requestLegacy({
      url
    }).request;

    promise.done((data) => {
      if (!isStateRecord(data) && !Array.isArray(data)) {
        dispatch(set({ section, isSchemaFetching: false, isSchemaPopulated: true, schemaError: new ApiError({ kind: 'invalid-response' }) }));
        return;
      }

      dispatch(set({
        section,
        isSchemaFetching: false,
        isSchemaPopulated: true,
        schemaError: null,
        schema: data
      }));
    });

    promise.fail((xhr) => {
      dispatch(set({
        section,
        isSchemaFetching: false,
        isSchemaPopulated: true,
        schemaError: xhr
      }));
    });
  };
}

export default createFetchSchemaHandler;
