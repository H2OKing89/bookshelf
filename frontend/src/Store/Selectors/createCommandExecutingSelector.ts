import { createSelector } from 'reselect';
import type { StateRecord } from 'Store/StateTypes';
import { isCommandExecuting } from 'Utilities/Command';
import createCommandSelector from './createCommandSelector';

function createCommandExecutingSelector(name: string, constraints: StateRecord = {}) {
  return createSelector(createCommandSelector(name, constraints), isCommandExecuting);
}
export default createCommandExecutingSelector;
