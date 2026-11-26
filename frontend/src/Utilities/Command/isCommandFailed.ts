function isCommandFailed(command?: { status: string } | null): boolean {
  return !!command && (['failed', 'aborted', 'cancelled', 'orphaned'].includes(command.status));
}
export default isCommandFailed;
