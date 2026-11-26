import isSameCommand from './isSameCommand';

function findCommand<T extends { body?: unknown }>(commands: readonly T[], options: unknown): T | undefined {
  return commands.findLast((command) => isSameCommand(command.body, options));
}
export default findCommand;
