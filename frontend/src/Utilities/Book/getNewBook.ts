import { asStateRecord, isStateRecord, type StateRecord } from 'Store/StateTypes';
import getNewAuthor from 'Utilities/Author/getNewAuthor';

export default function getNewBook(book: StateRecord, payload: StateRecord): StateRecord {
  if (!isStateRecord(book.author)) {
    throw new TypeError('Missing book author');
  }
  const { searchForNewBook = false } = payload;
  let author = book.author;
  if (!Object.hasOwn(author, 'id') || author.id === 0) {
    author = getNewAuthor(author, payload);
    if (payload.monitor === 'specificBook') {
      const addOptions = { ...asStateRecord(author.addOptions) };
      delete addOptions.monitor;
      author = { ...author, addOptions: { ...addOptions, booksToMonitor: [book.foreignBookId] } };
    }
  }
  return { ...book, author, addOptions: { searchForNewBook }, monitored: true };
}
