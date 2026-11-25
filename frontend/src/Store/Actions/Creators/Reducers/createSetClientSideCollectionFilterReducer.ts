import { isStateRecord, StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';

function createSetClientSideCollectionFilterReducer(section: string) {
  return (state: StateRecord, { payload }: { payload: unknown }): StateRecord => {
    if (!isStateRecord(payload)) {
      return state;
    }

    const { selectedFilterKey } = payload;
    if (selectedFilterKey != null && typeof selectedFilterKey !== 'string' && typeof selectedFilterKey !== 'number') {
      return state;
    }

    return updateSectionState(state, section, {
      ...getSectionState(state, section),
      selectedFilterKey
    });
  };
}

export default createSetClientSideCollectionFilterReducer;
