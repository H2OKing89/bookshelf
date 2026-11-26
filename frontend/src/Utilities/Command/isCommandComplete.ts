function isCommandComplete(command?: { status: string } | null): boolean {
  return !!command && (command.status === 'complete');
}
export default isCommandComplete;
