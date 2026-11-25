import _ from 'lodash';
import { isStateRecord, StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';

const whitelistedProperties = ['pageSize', 'columns', 'tableOptions'];

function createSetTableOptionReducer(section: string) {
  return (state: StateRecord, { payload }: { payload: unknown }): StateRecord => {
    if (!isStateRecord(payload)) {
      return state;
    }

    const newState = {
      ...getSectionState(state, section),
      ..._.pick(payload, whitelistedProperties)
    };
    return updateSectionState(state, section, newState);
  };
}

export default createSetTableOptionReducer;
