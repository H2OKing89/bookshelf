function isCommandExecuting(command?: { status: string } | null): boolean {
  return !!command && (command.status === 'queued' || command.status === 'started');
}
export default isCommandExecuting;
