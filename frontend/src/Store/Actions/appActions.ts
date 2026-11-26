import { createAction } from 'redux-actions';
import { asStateRecord, isItemId, isStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';
import { fetchTranslations as fetchAppTranslations } from 'Utilities/String/translate';
import createHandleActions from './Creators/createHandleActions';
import { requestLegacy } from './Creators/legacyRequest';

function getDimensions(width: number, height: number) {
  const dimensions = {
    width,
    height,
    isExtraSmallScreen: width <= 480,
    isSmallScreen: width <= 768,
    isMediumScreen: width <= 1310,
    isLargeScreen: width <= 1200
  };

  return dimensions;
}

//
// Variables

export const section = 'app';
const messagesSection = 'app.messages';
interface PingState {
  abort?: () => void;
  timeout?: ReturnType<typeof setTimeout>;
}
const pings = new WeakMap<() => LegacyRootState, PingState>();

//
// State

export const defaultState = {
  dimensions: getDimensions(window.innerWidth, window.innerHeight),
  messages: {
    items: []
  },
  version: window.Readarr.version,
  isUpdated: false,
  isConnected: true,
  isReconnecting: false,
  isDisconnected: false,
  isRestarting: false,
  isSidebarVisible: !getDimensions(window.innerWidth, window.innerHeight).isSmallScreen,
  translations: {
    isFetching: true,
    isPopulated: false,
    error: null
  }
};

//
// Action Types

export const SHOW_MESSAGE = 'app/showMessage';
export const HIDE_MESSAGE = 'app/hideMessage';
export const SAVE_DIMENSIONS = 'app/saveDimensions';
export const SET_VERSION = 'app/setVersion';
export const SET_APP_VALUE = 'app/setAppValue';
export const SET_IS_SIDEBAR_VISIBLE = 'app/setIsSidebarVisible';
export const FETCH_TRANSLATIONS = 'app/fetchTranslations';

export const PING_SERVER = 'app/pingServer';

//
// Action Creators

export const saveDimensions = createAction<{ width: number; height: number }>(SAVE_DIMENSIONS);
export const setVersion = createAction<{ version: string }>(SET_VERSION);
export const setIsSidebarVisible = createAction<{ isSidebarVisible: boolean }>(SET_IS_SIDEBAR_VISIBLE);
export const setAppValue = createAction<StateRecord>(SET_APP_VALUE);
export const showMessage = createAction<StateRecord>(SHOW_MESSAGE);
export const hideMessage = createAction<{ id: number | string }>(HIDE_MESSAGE);
export const pingServer = createThunk(PING_SERVER);
export const fetchTranslations = createThunk(FETCH_TRANSLATIONS);

//
// Helpers

function pingServerAfterTimeout(getState: () => LegacyRootState, dispatch: LegacyDispatch) {
  const previous = pings.get(getState);
  const attempt: PingState = {};
  pings.set(getState, attempt);
  if (previous?.timeout !== undefined) {
    clearTimeout(previous.timeout);
  }
  previous?.abort?.();

  attempt.timeout = setTimeout(() => {
    const app = getState().app;
    if (app.isRestarting !== true && app.isConnected === true) {
      pings.delete(getState);
      return;
    }

    const { request, abortRequest } = requestLegacy({ url: '/system/status', method: 'GET', contentType: 'application/json' });
    attempt.abort = abortRequest;

    const finish = () => {
      if (pings.get(getState) !== attempt) {
        return false;
      }
      pings.delete(getState);
      return true;
    };

    request.done(() => {
      if (finish()) {
        dispatch(setAppValue({ isRestarting: false }));
      }
    });

    request.fail((xhr) => {
      if (!finish()) {
        return;
      }
      if (asStateRecord(xhr).status === 401) {
        dispatch(setAppValue({ isRestarting: false }));
      } else {
        pingServerAfterTimeout(getState, dispatch);
      }
    });
  }, 5000);
}

//
// Action Handlers

export const actionHandlers = handleThunks({
  [PING_SERVER]: function(getState, _payload, dispatch) {
    pingServerAfterTimeout(getState, dispatch);
  },
  [FETCH_TRANSLATIONS]: async function(_getState, _payload, dispatch) {
    const isFetchingComplete = await fetchAppTranslations();

    dispatch(setAppValue({
      translations: {
        isFetching: false,
        isPopulated: isFetchingComplete,
        error: isFetchingComplete ? null : 'Failed to load translations from API'
      }
    }));
  }
});

//
// Reducers

export const reducers = createHandleActions({

  [SAVE_DIMENSIONS]: function(state, { payload }) {
    if (!isStateRecord(payload) || typeof payload.width !== 'number' || typeof payload.height !== 'number' ||
        !Number.isFinite(payload.width) || !Number.isFinite(payload.height) || payload.width < 0 || payload.height < 0) {
      return state;
    }
    return { ...state, dimensions: getDimensions(payload.width, payload.height) };
  },

  [SHOW_MESSAGE]: function(state, { payload }) {
    if (!isStateRecord(payload) || !isItemId(payload.id)) {
      return state;
    }
    const messages = getSectionState(state, messagesSection);
    const items: unknown[] = Array.isArray(messages.items) ? messages.items : [];
    const index = items.findIndex((item) => asStateRecord(item).id === payload.id);
    const next = [...items];
    if (index >= 0) {
      next[index] = { ...asStateRecord(next[index]), ...payload };
    } else {
      next.push({ ...payload });
    }
    return updateSectionState(state, messagesSection, { ...messages, items: next });
  },

  [HIDE_MESSAGE]: function(state, { payload }) {
    if (!isStateRecord(payload) || !isItemId(payload.id)) {
      return state;
    }
    const messages = getSectionState(state, messagesSection);
    const items: unknown[] = Array.isArray(messages.items) ? messages.items : [];
    return updateSectionState(state, messagesSection, { ...messages, items: items.filter((item) => asStateRecord(item).id !== payload.id) });
  },

  [SET_APP_VALUE]: function(state, { payload }) {
    return isStateRecord(payload) ? updateSectionState(state, section, { ...getSectionState(state, section), ...payload }) : state;
  },

  [SET_VERSION]: function(state, { payload }) {
    if (!isStateRecord(payload) || typeof payload.version !== 'string') {
      return state;
    }
    const version = payload.version;
    const newState: StateRecord = { version };
    if (state.version !== version) {
      if (!state.prevVersion) {
        newState.prevVersion = state.version;
      }
      newState.isUpdated = true;
    }
    return { ...state, ...newState };
  },

  [SET_IS_SIDEBAR_VISIBLE]: function(state, { payload }) {
    return isStateRecord(payload) && typeof payload.isSidebarVisible === 'boolean' ?
      { ...state, isSidebarVisible: payload.isSidebarVisible } : state;
  }

}, defaultState, section);
