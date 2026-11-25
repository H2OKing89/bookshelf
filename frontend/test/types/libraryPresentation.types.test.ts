import type { ComponentProps } from 'react';
import type EditAuthorModal from 'Author/Edit/EditAuthorModal';
import type { AuthorId, BookFileId, BookId, EditionId } from 'Book/BookIdentity';
import type EditBookModal from 'Book/Edit/EditBookModal';
import type FileDetailsModal from 'BookFile/FileDetailsModal';
import type SelectEditionRow from 'InteractiveImport/Edition/SelectEditionRow';

export function checkPresentationIdentities(authorId: AuthorId, bookId: BookId, fileId: BookFileId, editionId: EditionId, select: ComponentProps<typeof SelectEditionRow>['onEditionSelect']) {
  const editAuthor: ComponentProps<typeof EditAuthorModal> = { authorId, isOpen: false, onModalClose: () => undefined };
  const editBook: ComponentProps<typeof EditBookModal> = { authorId, bookId, isOpen: false, onModalClose: () => undefined };
  const viewFile: ComponentProps<typeof FileDetailsModal> = { id: fileId, isOpen: true, onModalClose: () => undefined };
  select(bookId, 'provider-recording-uk');
  // @ts-expect-error Unlike the former overview wiring, book IDs cannot select author settings.
  const wrongAuthor: ComponentProps<typeof EditAuthorModal> = { ...editAuthor, authorId: bookId };
  // @ts-expect-error Edition IDs do not identify their parent book.
  const wrongBook: ComponentProps<typeof EditBookModal> = { ...editBook, bookId: editionId };
  // @ts-expect-error A file details modal cannot use a book ID.
  const wrongFile: ComponentProps<typeof FileDetailsModal> = { ...viewFile, id: bookId };
  // @ts-expect-error An imported recording is selected by foreign ID, not its numeric database ID.
  select(bookId, editionId);
  // @ts-expect-error An author cannot act as the parent book.
  select(authorId, 'local-recording');
  return { wrongAuthor, wrongBook, wrongFile };
}
