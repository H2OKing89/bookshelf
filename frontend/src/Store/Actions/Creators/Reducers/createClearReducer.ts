import { StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';

function createClearReducer(section: string, defaultState: StateRecord) {
  return (state: StateRecord): StateRecord => {
    return updateSectionState(state, section, {
      ...getSectionState(state, section),
      ...defaultState
    });
  };
}

export default createClearReducer;
