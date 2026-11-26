import { Component } from 'react';
import type { AuthorId, BookId } from 'Book/BookIdentity';
import IconButton from 'Components/Link/IconButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import BookInteractiveSearchModal from './Search/BookInteractiveSearchModal';
import styles from './BookSearchCell.css';

export interface BookSearchCellProps {
  bookId: BookId;
  authorId: AuthorId;
  bookTitle: string;
  authorName: string;
  isSearching: boolean;
  isDisabled?: boolean;
  onSearchPress: () => void;
}

class BookSearchCell extends Component<BookSearchCellProps, { isDetailsModalOpen: boolean }> {

  //
  // Lifecycle

  constructor(props: BookSearchCellProps) {
    super(props);

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

        <BookInteractiveSearchModal
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

export default BookSearchCell;
