import { createSelector } from 'reselect';
import type { StateRecord } from 'Store/StateTypes';
import { findCommand } from 'Utilities/Command';
import createCommandsSelector from './createCommandsSelector';

function createCommandSelector(name: string, constraints: StateRecord = {}) {
  return createSelector(createCommandsSelector(), (commands) => findCommand(commands, { name, ...constraints }));
}
export default createCommandSelector;
