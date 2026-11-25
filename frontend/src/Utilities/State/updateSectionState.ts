import { StateRecord } from 'Store/StateTypes';

function updateSectionState(state: StateRecord, section: string, newState: StateRecord): StateRecord {
  const [, subSection] = section.split('.');

  if (subSection) {
    return { ...state, [subSection]: newState };
  }

  return Object.hasOwn(state, section) ?
    { ...state, [section]: newState } :
    { ...state, ...newState };
}

export default updateSectionState;
