import type { LegacyDispatch, LegacyRootState, StateRecord } from 'Store/StateTypes';
import { set } from '../baseActions';
import { type CollectionFetchThunk, isFilterKey } from './pagedCollection';

function createSetServerSideCollectionFilterHandler(section: string, fetchHandler: CollectionFetchThunk) {
  return function(_getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
    if (!isFilterKey(payload.selectedFilterKey)) {
      return;
    }
    dispatch(set({ section, selectedFilterKey: payload.selectedFilterKey }));
    dispatch(fetchHandler({ page: 1 }));
  };
}

export default createSetServerSideCollectionFilterHandler;
