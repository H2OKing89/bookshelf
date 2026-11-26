import { connect } from 'react-redux';
import type { BookId } from 'Book/BookIdentity';
import type { LibraryBook, LibraryEdition } from 'Book/LibraryModels';
import selectById, { type ResourceCollection } from 'Store/Selectors/selectById';
import BookRecordingMetadata from './BookRecordingMetadata';

interface State {
  books: ResourceCollection<LibraryBook>;
  editions: ResourceCollection<LibraryEdition>;
}

function mapStateToProps(state: State, { bookId }: { bookId: BookId }) {
  return {
    book: selectById(state.books, bookId),
    edition: state.editions.items.find((edition) => edition.bookId === bookId && edition.monitored === true)
  };
}

export default connect(mapStateToProps)(BookRecordingMetadata);
