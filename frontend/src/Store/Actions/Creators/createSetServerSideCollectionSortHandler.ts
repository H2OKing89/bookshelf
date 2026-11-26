import { ASCENDING, DESCENDING } from 'Helpers/Props/sortDirections';
import { asStateRecord, type LegacyDispatch, type LegacyRootState, type StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';
import { set } from '../baseActions';
import { type CollectionFetchThunk, isSortDirection } from './pagedCollection';

function createSetServerSideCollectionSortHandler(section: string, fetchHandler: CollectionFetchThunk) {
  return function(getState: () => LegacyRootState, payload: StateRecord, dispatch: LegacyDispatch) {
    const sectionState = asStateRecord(getSectionState(getState(), section, true));
    const sortKey = typeof payload.sortKey === 'string' && payload.sortKey ? payload.sortKey : sectionState.sortKey;
    let sortDirection = payload.sortDirection;
    if (!isSortDirection(sortDirection)) {
      if (payload.sortKey === sectionState.sortKey) {
        sortDirection = sectionState.sortDirection === ASCENDING ? DESCENDING : ASCENDING;
      } else {
        sortDirection = sectionState.sortDirection;
      }
    }
    dispatch(set({ section, sortKey, sortDirection }));
    dispatch(fetchHandler());
  };
}

export default createSetServerSideCollectionSortHandler;
