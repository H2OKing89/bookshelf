import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { set } from 'Store/Actions/baseActions';
import { asStateRecord, isStateRecord, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';
import createHandleActions from './Creators/createHandleActions';
import { requestLegacy } from './Creators/legacyRequest';
import { asPromise, providerRequest, requireRecord } from './Creators/operationalRequests';

export const section = 'oAuth';
const callbackUrl = `${window.location.origin}${window.Readarr.urlBase}/oauth.html`;

interface OAuthAttempt {
  cancel?: () => void;
}
const active = new WeakMap<() => LegacyRootState, OAuthAttempt>();

export const defaultState = { authorizing: false, result: null, error: null };

export const START_OAUTH = 'oAuth/startOAuth';
export const SET_OAUTH_VALUE = 'oAuth/setOAuthValue';
export const RESET_OAUTH = 'oAuth/resetOAuth';
const CLEAR_OAUTH = 'oAuth/clearOAuth';

export const startOAuth = createThunk(START_OAUTH);
export const setOAuthValue = createAction<StateRecord>(SET_OAUTH_VALUE);
export const resetOAuth = createThunk(RESET_OAUTH);
const clearOAuth = createAction<void>(CLEAR_OAUTH);

function showOAuthWindow(url: string, name: string, attempt: OAuthAttempt): Promise<StateRecord> {
  return new Promise((resolve, reject) => {
    const newWindow = window.open(url);
    if (!newWindow || newWindow.closed || typeof newWindow.closed === 'undefined') {
      reject({
        status: 400,
        responseJSON: [{
          propertyName: name,
          errorMessage: 'Pop-ups are being blocked by your browser'
        }]
      });
      return;
    }

    const callback = (query: string, onComplete: () => void) => {
      if (window.onCompleteOauth === callback) {
        delete window.onCompleteOauth;
      }
      attempt.cancel = undefined;
      // Preserve the legacy query representation expected by providers.
      const queryParams: StateRecord = Object.fromEntries(query.substring(1).split('&').filter(Boolean).map((param) => {
        const [key, value] = param.split('=');
        return [key, value];
      }));
      onComplete();
      resolve(queryParams);
    };

    attempt.cancel = () => {
      if (window.onCompleteOauth === callback) {
        delete window.onCompleteOauth;
      }
      if (!newWindow.closed) {
        newWindow.close();
      }
      reject(new ApiError({ kind: 'aborted' }));
    };
    window.onCompleteOauth = callback;
  });
}

async function executeIntermediateRequest(payload: StateRecord, options: StateRecord) {
  if (typeof options.url !== 'string') {
    throw new ApiError({ kind: 'invalid-response' });
  }
  // The provider supplies the existing intermediate request; no new transport
  // or credentials are introduced by the typed boundary.
  const { request } = requestLegacy({ ...options, url: options.url });
  const data = requireRecord(await asPromise(request));
  return requireRecord(await asPromise(providerRequest({
    ...payload,
    action: 'continueOAuth',
    queryParams: { ...data, callbackUrl }
  })));
}

export const actionHandlers = handleThunks({
  [START_OAUTH]: async function(getState, payload, dispatch) {
    const { name, section: actionSection, ...otherPayload } = payload;
    if (typeof name !== 'string' || typeof actionSection !== 'string') {
      return;
    }
    const previous = active.get(getState);
    const attempt: OAuthAttempt = {};
    active.set(getState, attempt);
    previous?.cancel?.();
    dispatch(setOAuthValue({ authorizing: true }));

    const isCurrent = () => active.get(getState) === attempt;
    try {
      let response = requireRecord(await asPromise(providerRequest({
        ...otherPayload, action: 'startOAuth', queryParams: { callbackUrl }
      })));
      if (!isCurrent()) {
        return;
      }
      if (typeof response.oauthUrl !== 'string') {
        response = await executeIntermediateRequest(otherPayload, response);
      }
      if (!isCurrent()) {
        return;
      }
      if (typeof response.oauthUrl !== 'string') {
        throw new ApiError({ kind: 'invalid-response' });
      }
      const queryParams = await showOAuthWindow(response.oauthUrl, name, attempt);
      if (!isCurrent()) {
        return;
      }
      const result = requireRecord(await asPromise(providerRequest({
        ...otherPayload, action: 'getOAuthToken', queryParams: { ...response, ...queryParams }
      })));
      if (isCurrent()) {
        dispatch(batchActions([
          setOAuthValue({ authorizing: false, result, error: null }),
          set({ section: actionSection, saveError: null })
        ]));
      }
    } catch (error) {
      if (isCurrent()) {
        const actions = [setOAuthValue({ authorizing: false, result: null, error })];
        if (asStateRecord(error).status === 400) {
          actions.unshift(set({ section: actionSection, saveError: error }));
        }
        dispatch(batchActions(actions));
      }
    } finally {
      if (isCurrent()) {
        active.delete(getState);
      }
    }
  },

  [RESET_OAUTH]: function(getState, _payload, dispatch) {
    const attempt = active.get(getState);
    active.delete(getState);
    attempt?.cancel?.();
    dispatch(clearOAuth());
  }
});

export const reducers = createHandleActions({
  [SET_OAUTH_VALUE]: (state, { payload }) => isStateRecord(payload) ?
    updateSectionState(state, section, { ...getSectionState(state, section), ...payload }) : state,
  [CLEAR_OAUTH]: (state) => updateSectionState(state, section, defaultState)
}, defaultState, section);
