import { combineReducers, Reducer } from 'redux';
import actions from 'Store/Actions';
import { isStateRecord, LegacyRootState, StateRecord, StoreAction } from 'Store/StateTypes';

const defaultState: LegacyRootState = {};
const reducers: Record<string, Reducer<StateRecord, StoreAction>> = {};

// The JS action modules are dynamic registrations. Check that boundary
// rather than asserting the still-partial typed application state.
for (const registration of actions as readonly unknown[]) {
  if (!isStateRecord(registration) || typeof registration.section !== 'string' ||
      !isStateRecord(registration.defaultState) || typeof registration.reducers !== 'function') {
    throw new Error('Invalid Redux section registration');
  }

  const { section, reducers: reducer } = registration;
  if (Object.hasOwn(reducers, section)) {
    throw new Error(`Duplicate Redux section: ${section}`);
  }

  defaultState[section] = registration.defaultState;
  reducers[section] = (state, action) => {
    const next: unknown = reducer(state, action);
    if (!isStateRecord(next)) {
      throw new Error(`Invalid Redux state returned for ${section}`);
    }
    return next;
  };
}

export { defaultState };

export default function createReducers(): Reducer<LegacyRootState, StoreAction> {
  const reduce = combineReducers(reducers);

  // Preserve the existing redux-batched-actions action envelope, without
  // relying on its untyped higher-order reducer.
  const reduceBatch: Reducer<LegacyRootState, StoreAction> = (state, action) => {
    if (isStateRecord(action.meta) && action.meta.batch && Array.isArray(action.payload)) {
      return action.payload.reduce((current: LegacyRootState | undefined, child: unknown) => {
        if (isStateRecord(child) && typeof child.type === 'string') {
          return reduceBatch(current, { ...child, type: child.type });
        }
        // Function thunks cannot run inside a reducer; dispatch those outside
        // batches. Ignore an invalid nested action rather than treating it as
        // an application's action.
        return current ?? reduce(undefined, { type: '@@bookshelf/INIT' });
      }, state) ?? reduce(undefined, { type: '@@bookshelf/INIT' });
    }

    return reduce(state, action);
  };

  return reduceBatch;
}
