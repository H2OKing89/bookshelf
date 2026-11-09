import { connect } from 'react-redux';
import BookRecordingMetadata from './BookRecordingMetadata';

function mapStateToProps(state, { bookId }) {
  return {
    book: state.books.items.find((book) => book.id === bookId),
    edition: state.editions.items.find((edition) => edition.bookId === bookId && edition.monitored)
  };
}

export default connect(mapStateToProps)(BookRecordingMetadata);
