import { createSelector } from 'reselect';
import { isCommandExecuting } from 'Utilities/Command';
import type { CommandCollection } from './selectorModels';

function createExecutingCommandsSelector() {
  return createSelector((state: { commands: CommandCollection }) => state.commands.items, (commands) => commands.filter(isCommandExecuting));
}
export default createExecutingCommandsSelector;
