import _ from 'lodash';
import { asStateRecord, StateRecord } from 'Store/StateTypes';

// Legacy full-tree queries may return any resource, whereas section reducers
// always work with a shallow record. Overloads make that distinction explicit.
/* eslint-disable no-redeclare */
function getSectionState(state: StateRecord, section: string, isFullStateTree: true): unknown;
function getSectionState(state: StateRecord, section: string, isFullStateTree?: false): StateRecord;
function getSectionState(state: StateRecord, section: string, isFullStateTree = false): unknown {
  if (isFullStateTree) {
    return _.get(state, section);
  }

  const [, subSection] = section.split('.');

  if (subSection) {
    return { ...asStateRecord(state[subSection]) };
  }

  // Older reducers also expose a state object named after their section.
  return Object.hasOwn(state, section) ?
    { ...asStateRecord(state[section]) } :
    { ...state };
}

/* eslint-enable no-redeclare */

export default getSectionState;
