import { asStateRecord, isStateRecord, StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';

function createSetProviderFieldValueReducer(section: string) {
  return (state: StateRecord, { payload }: { payload: unknown }): StateRecord => {
    if (!isStateRecord(payload) || section !== payload.section || typeof payload.name !== 'string') {
      return state;
    }

    const { name, value } = payload;
    const newState = getSectionState(state, section);
    const pendingChanges = asStateRecord(newState.pendingChanges);
    const fields = { ...asStateRecord(pendingChanges.fields), [name]: value };

    return updateSectionState(state, section, {
      ...newState,
      pendingChanges: { ...pendingChanges, fields }
    });
  };
}

export default createSetProviderFieldValueReducer;
