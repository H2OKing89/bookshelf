import { LegacyDispatch, LegacyRootState, LegacyThunk, StateRecord } from './StateTypes';

export type ThunkHandler = (
  getState: () => LegacyRootState,
  payload: StateRecord,
  dispatch: LegacyDispatch
) => unknown;

const thunks = new Map<string, ThunkHandler>();

function identity(payload: StateRecord) {
  return payload;
}

export function createThunk(
  type: string,
  identityFunction: (payload: StateRecord) => StateRecord = identity
) {
  return (payload: StateRecord = {}): LegacyThunk => (dispatch, getState) => {
    const thunk = thunks.get(type);

    if (thunk) {
      return thunk(getState, identityFunction(payload), dispatch);
    }

    throw Error(`Thunk handler has not been registered for ${type}`);
  };
}

export function handleThunks(handlers: Record<string, ThunkHandler>) {
  Object.entries(handlers).forEach(([type, handler]) => {
    thunks.set(type, handler);
  });
}
