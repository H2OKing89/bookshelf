import { createSelector } from 'reselect';
import type { CommandCollection } from './selectorModels';

function createCommandsSelector() {
  return createSelector((state: { commands: CommandCollection }) => state.commands, (commands) => commands.items);
}
export default createCommandsSelector;
