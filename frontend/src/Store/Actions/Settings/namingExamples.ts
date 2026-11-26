import { batchActions } from 'redux-batched-actions';
import { set, update } from 'Store/Actions/baseActions';
import { requestLegacy } from 'Store/Actions/Creators/legacyRequest';
import { createSectionRequests } from 'Store/Actions/Creators/sectionRequests';
import { asStateRecord, isStateRecord } from 'Store/StateTypes';
import { createThunk, type ThunkHandler } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getSectionState from 'Utilities/State/getSectionState';

const section = 'settings.namingExamples';
const requests = createSectionRequests();

export const FETCH_NAMING_EXAMPLES = 'settings/namingExamples/fetchNamingExamples';
export const fetchNamingExamples = createThunk(FETCH_NAMING_EXAMPLES);

const fetchExamples: ThunkHandler = (getState, _payload, dispatch) => {
  const naming = asStateRecord(getSectionState(getState(), 'settings.naming', true));
  const data = { ...asStateRecord(naming.item), ...asStateRecord(naming.pendingChanges) };
  const attempt = requests.begin(getState, section);
  dispatch(set({ section, isFetching: true }));

  const { request, abortRequest } = requestLegacy({ url: '/config/naming/examples', data });
  attempt.attachAbort(abortRequest);

  request.done((response) => {
    if (!attempt.finish()) {
      return;
    }
    if (!isStateRecord(response) || !['singleBookExample', 'multiPartBookExample', 'authorFolderExample'].every((name) =>
      response[name] == null || typeof response[name] === 'string'
    )) {
      dispatch(set({ section, isFetching: false, isPopulated: false, error: new ApiError({ kind: 'invalid-response' }) }));
      return;
    }
    dispatch(batchActions([
      update({ section, data: response }),
      set({ section, isFetching: false, isPopulated: true, error: null })
    ]));
  });

  request.fail((xhr) => {
    if (attempt.finish()) {
      dispatch(set({ section, isFetching: false, isPopulated: false, error: isStateRecord(xhr) && xhr.aborted === true ? null : xhr }));
    }
  });
};

export default {
  defaultState: { isFetching: false, isPopulated: false, error: null, item: {} },
  actionHandlers: { [FETCH_NAMING_EXAMPLES]: fetchExamples },
  reducers: {}
};
