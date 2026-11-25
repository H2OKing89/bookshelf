import { asStateRecord, isStateRecord, StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';

function createSetSettingValueReducer(section: string) {
  return (state: StateRecord, { payload }: { payload: unknown }): StateRecord => {
    if (!isStateRecord(payload) || section !== payload.section || typeof payload.name !== 'string') {
      return state;
    }

    const { name, value } = payload;
    const newState = getSectionState(state, section);
    const pendingChanges = { ...asStateRecord(newState.pendingChanges) };
    const currentValue = newState.item ? asStateRecord(newState.item)[name] : null;
    let parsedValue: unknown = value;

    if (typeof currentValue === 'number' && value != null) {
      if (typeof value !== 'string' && typeof value !== 'number') {
        return state;
      }
      parsedValue = parseInt(String(value));
      if (!Number.isFinite(parsedValue)) {
        // Preserve an empty/invalid draft rather than writing NaN to JSON.
        parsedValue = value;
      }
    }

    if (currentValue === parsedValue) {
      delete pendingChanges[name];
    } else {
      pendingChanges[name] = parsedValue;
    }

    return updateSectionState(state, section, { ...newState, pendingChanges });
  };
}

export default createSetSettingValueReducer;
