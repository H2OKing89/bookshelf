import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { asStateRecord, isItemId, isStateRecord, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getSectionState from 'Utilities/State/getSectionState';
import { set, update } from './baseActions';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import { requestLegacy } from './Creators/legacyRequest';
import createClearReducer from './Creators/Reducers/createClearReducer';
import { createSectionRequests } from './Creators/sectionRequests';

//
// Variables

export const section = 'queue';
const status = `${section}.status`;
const details = `${section}.details`;

//
// State

export const defaultState = {
  status: {
    isFetching: false,
    isPopulated: false,
    error: null,
    item: {}
  },

  details: {
    isFetching: false,
    isPopulated: false,
    error: null,
    items: [],
    itemMap: {},
    params: {}
  }
};

const FETCH_QUEUE_STATUS = 'queue/fetchQueueStatus';
const FETCH_QUEUE_DETAILS = 'queue/fetchQueueDetails';
const CLEAR_QUEUE_DETAILS = 'queue/clearQueueDetails';

export const fetchQueueStatus = createThunk(FETCH_QUEUE_STATUS);
export const fetchQueueDetails = createThunk(FETCH_QUEUE_DETAILS);
export const clearQueueDetails = createThunk(CLEAR_QUEUE_DETAILS);
const clearDetailsAction = createAction(CLEAR_QUEUE_DETAILS);

//
// Helpers

const requests = createSectionRequests();

function parseStatus(value: unknown) {
  if (!isStateRecord(value)) {
    throw new TypeError('Invalid queue status');
  }
  return value;
}

export const actionHandlers = handleThunks({
  [FETCH_QUEUE_STATUS]: createFetchHandler(status, '/queue/status', parseStatus),
  [FETCH_QUEUE_DETAILS]: (getState, payload, dispatch) => {
    let params: StateRecord = payload;
    if (Object.keys(params).length) {
      dispatch(set({ section: details, params }));
    } else {
      params = asStateRecord(asStateRecord(getSectionState(getState(), details, true)).params);
    }
    if (!Object.keys(params).length) {
      return;
    }
    const attempt = requests.begin(getState, details);
    dispatch(set({ section: details, isFetching: true }));
    const { request, abortRequest } = requestLegacy({ url: '/queue/details', data: params, traditional: true });
    attempt.attachAbort(abortRequest);
    request.done((data) => {
      if (!attempt.finish()) {
        return;
      }
      if (!Array.isArray(data) || !data.every((item: unknown) => isStateRecord(item) && isItemId(item.id))) {
        dispatch(set({ section: details, isFetching: false, isPopulated: false, error: new ApiError({ kind: 'invalid-response' }) }));
        return;
      }
      dispatch(batchActions([update({ section: details, data }), set({ section: details, isFetching: false, isPopulated: true, error: null })]));
    });
    request.fail((xhr) => {
      if (attempt.finish()) {
        dispatch(set({ section: details, isFetching: false, isPopulated: false, error: isStateRecord(xhr) && xhr.aborted === true ? null : xhr }));
      }
    });
  },
  [CLEAR_QUEUE_DETAILS]: (getState, _payload, dispatch) => {
    requests.cancel(getState, details);
    dispatch(clearDetailsAction());
  }
});

//
// Reducers

export const reducers = createHandleActions({
  [CLEAR_QUEUE_DETAILS]: createClearReducer(details, defaultState.details)
}, defaultState, section);
