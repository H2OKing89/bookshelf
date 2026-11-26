import PropTypes from 'prop-types';
import { Component } from 'react';
import { connect } from 'react-redux';
import { useNavigate } from 'react-router-dom';
import { createSelector } from 'reselect';
import { deleteBook } from 'Store/Actions/bookActions';
import createBookSelector from 'Store/Selectors/createBookSelector';
import DeleteBookModalContent from './DeleteBookModalContent';

function createMapStateToProps() {
  return createSelector(
    createBookSelector(),
    (book) => {
      return book;
    }
  );
}

const mapDispatchToProps = {
  deleteBook
};

class DeleteBookModalConnector extends Component {

  //
  // Listeners

  onDeletePress = (deleteFiles, addImportListExclusion) => {
    this.props.deleteBook({
      id: this.props.bookId,
      deleteFiles,
      addImportListExclusion
    });

    this.props.onModalClose(true);

    this.props.navigate(`/author/${this.props.authorSlug}`);
  };

  //
  // Render

  render() {
    return (
      <DeleteBookModalContent
        {...this.props}
        onDeletePress={this.onDeletePress}
      />
    );
  }
}

DeleteBookModalConnector.propTypes = {
  bookId: PropTypes.number.isRequired,
  authorSlug: PropTypes.string.isRequired,
  navigate: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired,
  deleteBook: PropTypes.func.isRequired
};

const ConnectedDeleteBook = connect(createMapStateToProps, mapDispatchToProps)(DeleteBookModalConnector);

export default function DeleteBookModalContentConnector(props) {
  const navigate = useNavigate();

  return <ConnectedDeleteBook {...props} navigate={navigate} />;
}
