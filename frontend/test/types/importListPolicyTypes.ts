import type { EditionId } from 'Book/BookIdentity';
import type { CleanupAction, CleanupCandidate, MembershipItem } from 'Settings/ImportLists/importListPolicyQuery';

// The provider payload uses qualified strings and never local numeric IDs.
export function rejectMixup(item: MembershipItem, candidate: CleanupCandidate, editionId: EditionId) {
  // @ts-expect-error Local edition IDs are not cleanup book IDs.
  candidate.bookId = editionId;
  // @ts-expect-error A resolved provider ID is not a local book ID.
  candidate.bookId = item.bookId;
  // @ts-expect-error A local numeric book ID is not source/provider identity.
  item.sourceBookId = candidate.bookId;
  // @ts-expect-error Preview mode never has a confirmable action.
  const preview: CleanupAction = { mode: 'preview', tagId: null, confirmationToken: 'token' };
  // @ts-expect-error A tag action needs a specific tag ID.
  const tag: CleanupAction = { mode: 'tag', tagId: null, confirmationToken: 'token' };
  return { preview, tag };
}
