import _ from 'lodash';
import { createAction } from 'redux-actions';
import { asStateRecord, isStateRecord, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import { createThunk, handleThunks } from 'Store/thunks';
import { ApiError } from 'Utilities/Fetch/fetchJson';
import updateSectionState from 'Utilities/State/updateSectionState';
import { set } from './baseActions';
import createHandleActions from './Creators/createHandleActions';
import { providerRequest } from './Creators/operationalRequests';

export const section = 'providerOptions';
const lastActions = new WeakMap<() => LegacyRootState, Map<string, { id: number; payload: StateRecord }>>();
let lastActionId = 0;

export const defaultState = {
  items: [],
  isFetching: false,
  isPopulated: false,
  error: false
};

export const FETCH_OPTIONS = 'providers/fetchOptions';
export const CLEAR_OPTIONS = 'providers/clearOptions';

export const fetchOptions = createThunk(FETCH_OPTIONS);
export const clearOptions = createAction<{ section: string }>(CLEAR_OPTIONS);

export const actionHandlers = handleThunks({
  [FETCH_OPTIONS]: function(getState, payload, dispatch) {
    if (typeof payload.section !== 'string' || !payload.section) {
      return;
    }

    const name = payload.section;
    const subsection = `${section}.${name}`;
    let actions = lastActions.get(getState);
    if (!actions) {
      actions = new Map();
      lastActions.set(getState, actions);
    }

    const previous = actions.get(name);
    const current = () => asStateRecord(asStateRecord(getState().providerOptions)[name]);
    if (previous && current().requestId === previous.id && _.isEqual(payload, previous.payload)) {
      return;
    }

    const actionId = ++lastActionId;
    const pending = actions;
    pending.set(name, { id: actionId, payload });
    dispatch(set({ section: subsection, isFetching: true, requestId: actionId }));

    const finish = (error: unknown, items?: unknown[]) => {
      if (pending.get(name)?.id !== actionId) {
        return;
      }
      pending.delete(name);
      // Clearing or changing provider options invalidates the response.
      if (current().requestId !== actionId) {
        return;
      }
      dispatch(set({
        section: subsection,
        isFetching: false,
        isPopulated: items !== undefined,
        error: items === undefined ? (error ?? new ApiError({ kind: 'network' })) : null,
        requestId: null,
        ...(items && { items })
      }));
    };

    try {
      const request = providerRequest(payload);
      request.done((data) => {
        if (!isStateRecord(data) || (data.options != null && !Array.isArray(data.options))) {
          finish(new ApiError({ kind: 'invalid-response' }));
          return;
        }
        finish(null, Array.isArray(data.options) ? data.options : []);
      });
      request.fail((error) => finish(error));
    } catch (error) {
      finish(error);
    }
  }
});

export const reducers = createHandleActions({
  [CLEAR_OPTIONS]: function(state, { payload }) {
    if (!isStateRecord(payload) || typeof payload.section !== 'string') {
      return state;
    }
    return updateSectionState(state, `${section}.${payload.section}`, defaultState);
  }
}, {}, section);
