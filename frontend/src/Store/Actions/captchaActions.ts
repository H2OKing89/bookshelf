import { createAction } from 'redux-actions';
import { isStateRecord, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';
import createHandleActions from './Creators/createHandleActions';
import { providerRequest } from './Creators/operationalRequests';
import { createSectionRequests } from './Creators/sectionRequests';

export const section = 'captcha';
const requests = createSectionRequests();

export const defaultState = {
  refreshing: false,
  token: null,
  siteKey: null,
  secretToken: null,
  ray: null,
  stoken: null,
  responseUrl: null
};

export const REFRESH_CAPTCHA = 'captcha/refreshCaptcha';
export const GET_CAPTCHA_COOKIE = 'captcha/getCaptchaCookie';
export const SET_CAPTCHA_VALUE = 'captcha/setCaptchaValue';
export const RESET_CAPTCHA = 'captcha/resetCaptcha';
const CLEAR_CAPTCHA = 'captcha/clearCaptcha';

export const refreshCaptcha = createThunk(REFRESH_CAPTCHA);
export const getCaptchaCookie = createThunk(GET_CAPTCHA_COOKIE);
export const setCaptchaValue = createAction<StateRecord>(SET_CAPTCHA_VALUE);
export const resetCaptcha = createThunk(RESET_CAPTCHA);
const clearCaptcha = createAction<void>(CLEAR_CAPTCHA);

export const actionHandlers = handleThunks({
  [REFRESH_CAPTCHA]: function(getState, payload, dispatch) {
    const attempt = requests.begin(getState, section);
    dispatch(setCaptchaValue({ refreshing: true }));
    const finish = (value: StateRecord) => {
      if (attempt.finish()) {
        dispatch(setCaptchaValue({ ...value, refreshing: false }));
      }
    };

    try {
      const request = providerRequest({ ...payload, action: 'checkCaptcha' });
      request.done((data) => {
        finish(isStateRecord(data) && isStateRecord(data.captchaRequest) ? data.captchaRequest : {});
      });
      request.fail(() => finish({}));
    } catch {
      finish({});
    }
  },

  [GET_CAPTCHA_COOKIE]: function(getState, payload, dispatch) {
    // A user-completed response is required by the existing provider flow.
    if (typeof payload.captchaResponse !== 'string' || !payload.captchaResponse) {
      return;
    }
    const state = getState().captcha;
    const attempt = requests.begin(getState, section);
    const queryParams = {
      responseUrl: state.responseUrl,
      ray: state.ray,
      captchaResponse: payload.captchaResponse
    };

    try {
      const request = providerRequest({ ...payload, action: 'getCaptchaCookie', queryParams });
      request.done((data) => {
        if (attempt.finish() && isStateRecord(data) && typeof data.captchaToken === 'string') {
          dispatch(setCaptchaValue({ token: data.captchaToken }));
        }
      });
      request.fail(() => { attempt.finish(); });
    } catch {
      attempt.finish();
    }
  },

  [RESET_CAPTCHA]: function(getState, _payload, dispatch) {
    requests.cancel(getState, section);
    dispatch(clearCaptcha());
  }
});

export const reducers = createHandleActions({
  [SET_CAPTCHA_VALUE]: (state, { payload }) => isStateRecord(payload) ?
    updateSectionState(state, section, { ...getSectionState(state, section), ...payload }) : state,
  [CLEAR_CAPTCHA]: (state) => updateSectionState(state, section, defaultState)
}, defaultState, section);
