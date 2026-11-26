import { Component } from 'react';
import type { BookFileId } from 'Book/BookIdentity';
import FileDetailsModal from 'BookFile/FileDetailsModal';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './BookFileActionsCell.css';

interface BookFileActionsCellProps {
  id: BookFileId;
  path?: string | null;
  deleteBookFile: (options: { id: BookFileId }) => void;
}

interface BookFileActionsCellState {
  isDetailsModalOpen: boolean;
  isConfirmDeleteModalOpen: boolean;
}

class BookFileActionsCell extends Component<BookFileActionsCellProps, BookFileActionsCellState> {

  //
  // Lifecycle

  constructor(props: BookFileActionsCellProps) {
    super(props);

    this.state = {
      isDetailsModalOpen: false,
      isConfirmDeleteModalOpen: false
    };
  }

  //
  // Listeners

  onDetailsPress = () => {
    this.setState({ isDetailsModalOpen: true });
  };

  onDetailsModalClose = () => {
    this.setState({ isDetailsModalOpen: false });
  };

  onDeleteFilePress = () => {
    this.setState({ isConfirmDeleteModalOpen: true });
  };

  onConfirmDelete = () => {
    this.setState({ isConfirmDeleteModalOpen: false });
    this.props.deleteBookFile({ id: this.props.id });
  };

  onConfirmDeleteModalClose = () => {
    this.setState({ isConfirmDeleteModalOpen: false });
  };

  //
  // Render

  render() {

    const {
      id,
      path
    } = this.props;

    const {
      isDetailsModalOpen,
      isConfirmDeleteModalOpen
    } = this.state;

    return (
      <TableRowCell className={styles.TrackActionsCell}>
        {
          path &&
            <IconButton
              name={icons.INFO}
              aria-label={translate('FileDetails')}
              onPress={this.onDetailsPress}
            />
        }
        {
          path &&
            <IconButton
              name={icons.DELETE}
              aria-label={translate('DeleteBookFile')}
              onPress={this.onDeleteFilePress}
            />
        }

        <FileDetailsModal
          isOpen={isDetailsModalOpen}
          onModalClose={this.onDetailsModalClose}
          id={id}
        />

        <ConfirmModal
          isOpen={isConfirmDeleteModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteBookFile')}
          message={translate('DeleteBookFileMessageText', { 0: path ?? '' })}
          confirmLabel={translate('Delete')}
          onConfirm={this.onConfirmDelete}
          onCancel={this.onConfirmDeleteModalClose}
        />
      </TableRowCell>

    );
  }
}

export default BookFileActionsCell;
