import { type QueueItem, type QueueTarget, queueTarget } from 'Activity/Queue/queuePageQuery';
import type { AuthorId, BookFileId, BookId, EditionId } from 'Book/BookIdentity';
import { parseLibraryBook, parseLibraryBookFile, parseLibraryEdition } from 'Book/LibraryModels';
import { recordingCoverage, recordingsForBook, saveWorkCoverage } from 'Book/WorkCoverage/recordingWorkQuery';
import { setWantedMonitoring } from 'Wanted/wantedPageQuery';

export function checkLibraryIdentityTypes(value: unknown, authorId: AuthorId, fileId: BookFileId, editionId: EditionId, queueItem: QueueItem) {
  const book = parseLibraryBook(value);
  const edition = parseLibraryEdition(value);
  const file = parseLibraryBookFile(value);
  const id: BookId = book.id;
  const target = queueTarget(queueItem);
  const validQueueTarget: QueueTarget = { id: target.id, revision: target.revision };
  const ebook = edition.isEbook;
  const isbn = edition.isbn13;

  recordingsForBook(id);
  recordingCoverage(editionId);
  setWantedMonitoring([id], false);

  // @ts-expect-error Author IDs cannot select a book or its work coverage.
  recordingsForBook(authorId);
  // @ts-expect-error A file ID cannot select an edition.
  recordingCoverage(fileId);
  // @ts-expect-error An edition ID cannot change monitoring on the parent book.
  setWantedMonitoring([edition.id], false);
  // @ts-expect-error A file can be unassigned; zero/null is not a valid book ID.
  const mappedBook: BookId = file.bookId;
  // @ts-expect-error Missing runtime metadata must not become a known numeric value.
  const runtime: number = book.runtimeLengthMinutes;
  // @ts-expect-error A foreign recording identity may be absent.
  const foreignId: string = edition.foreignEditionId;
  // @ts-expect-error Queue revisions and row IDs are not interchangeable.
  const swapped: QueueTarget = { id: target.revision, revision: target.id };
  // @ts-expect-error A local book ID cannot act as a queue download identifier.
  const wrongTarget: QueueTarget = { id, revision: target.revision };
  // @ts-expect-error Editing work coverage requires an edition, not a book.
  saveWorkCoverage(book.id, 'review', []);
  // @ts-expect-error Unexpected API field names fail in strict consumers.
  const misspelled = file.qualityRanks;

  return { validQueueTarget, ebook, isbn, mappedBook, runtime, foreignId, swapped, wrongTarget, misspelled };
}
