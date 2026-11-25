import { createStore } from 'redux';
import createReducers, { defaultState } from 'Store/Actions/createReducers';
import createPersistState from 'Store/Middleware/createPersistState';
import middlewares from 'Store/Middleware/middlewares';
import { LegacyDispatch, LegacyRootState, StoreAction } from 'Store/StateTypes';

function createAppStore() {
  const persistence = createPersistState(defaultState);
  const appStore = createStore<LegacyRootState, StoreAction, { dispatch: LegacyDispatch }>(
    createReducers(), persistence.initialState, middlewares()
  );
  persistence.attach(appStore);
  return appStore;
}

export default createAppStore;
