import PropTypes from 'prop-types';
import React, { Component } from 'react';
import IconButton from 'Components/Link/IconButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import BookInteractiveSearchModalConnector from './Search/BookInteractiveSearchModalConnector';
import styles from './BookSearchCell.css';

class BookSearchCell extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isDetailsModalOpen: false
    };
  }

  componentDidUpdate() {
    if (this.props.isDisabled && this.state.isDetailsModalOpen) {
      this.onDetailsModalClose();
    }
  }

  //
  // Listeners

  onManualSearchPress = () => {
    if (!this.props.isDisabled) {
      this.setState({ isDetailsModalOpen: true });
    }
  };

  onDetailsModalClose = () => {
    this.setState({ isDetailsModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      bookId,
      bookTitle,
      authorName,
      isSearching,
      isDisabled,
      onSearchPress,
      ...otherProps
    } = this.props;

    return (
      <TableRowCell className={styles.BookSearchCell}>
        <SpinnerIconButton
          name={icons.SEARCH}
          isSpinning={isSearching}
          isDisabled={isDisabled}
          aria-label={translate('SearchBook')}
          onPress={isDisabled ? undefined : onSearchPress}
        />

        <IconButton
          name={icons.INTERACTIVE}
          isDisabled={isDisabled}
          aria-label={translate('InteractiveSearchModalHeader')}
          onPress={this.onManualSearchPress}
        />

        <BookInteractiveSearchModalConnector
          isOpen={this.state.isDetailsModalOpen && !isDisabled}
          bookId={bookId}
          bookTitle={bookTitle}
          authorName={authorName}
          onModalClose={this.onDetailsModalClose}
          {...otherProps}
        />

      </TableRowCell>
    );
  }
}

BookSearchCell.propTypes = {
  bookId: PropTypes.number.isRequired,
  authorId: PropTypes.number.isRequired,
  bookTitle: PropTypes.string.isRequired,
  authorName: PropTypes.string.isRequired,
  isSearching: PropTypes.bool.isRequired,
  isDisabled: PropTypes.bool,
  onSearchPress: PropTypes.func.isRequired
};

export default BookSearchCell;
