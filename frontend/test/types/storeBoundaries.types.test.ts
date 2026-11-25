import type { AuthorId, BookFileId, BookId } from 'Book/BookIdentity';
import type { LibraryBook, LibraryBookFile } from 'Book/LibraryModels';
import { removeItem, updateItem } from 'Store/Actions/baseActions';
import createBookFileSelector from 'Store/Selectors/createBookFileSelector';
import createBookSelector from 'Store/Selectors/createBookSelector';
import type { LegacyDispatch, LegacyThunk } from 'Store/StateTypes';

export function checkStoreTypes(
  dispatch: LegacyDispatch,
  bookId: BookId,
  authorId: AuthorId,
  bookFileId: BookFileId,
  state: { books: { items: LibraryBook[] }; bookFiles: { items: LibraryBookFile[] } }
) {
  const result: number = dispatch(() => 0);
  const thunk: LegacyThunk<boolean> = (_dispatch, getState) => !!getState().books;
  const flag: boolean = dispatch(thunk);
  dispatch(updateItem({ section: 'books', id: bookId, monitored: false }));
  dispatch(removeItem({ section: 'bookFiles', id: bookFileId }));
  const book = createBookSelector()(state, { bookId });
  const file = createBookFileSelector()(state, { bookFileId });

  // @ts-expect-error A thunk must be a function, not an unlabelled payload.
  dispatch({ id: bookId });
  // @ts-expect-error Shared item actions require a concrete ID.
  dispatch(updateItem({ section: 'books', monitored: false }));
  // @ts-expect-error Author IDs cannot select books.
  createBookSelector()(state, { bookId: authorId });
  // @ts-expect-error Book IDs cannot select recording files.
  createBookFileSelector()(state, { bookFileId: bookId });
  // @ts-expect-error The selected book might not exist.
  const requiredBook: LibraryBook = book;
  return { result, flag, book, file, requiredBook };
}
