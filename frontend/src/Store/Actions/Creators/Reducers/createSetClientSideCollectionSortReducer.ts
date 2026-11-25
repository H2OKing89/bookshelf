import { sortDirections } from 'Helpers/Props';
import { isStateRecord, StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';

function createSetClientSideCollectionSortReducer(section: string) {
  return (state: StateRecord, { payload }: { payload: unknown }): StateRecord => {
    if (!isStateRecord(payload)) {
      return state;
    }

    const newState = getSectionState(state, section);
    const sortKey = typeof payload.sortKey === 'string' && payload.sortKey ? payload.sortKey : newState.sortKey;
    let sortDirection = payload.sortDirection;

    if (sortDirection !== sortDirections.ASCENDING && sortDirection !== sortDirections.DESCENDING) {
      if (payload.sortKey === newState.sortKey) {
        sortDirection = newState.sortDirection === sortDirections.ASCENDING ?
          sortDirections.DESCENDING :
          sortDirections.ASCENDING;
      } else {
        sortDirection = newState.sortDirection;
      }
    }

    return updateSectionState(state, section, { ...newState, sortKey, sortDirection });
  };
}

export default createSetClientSideCollectionSortReducer;
