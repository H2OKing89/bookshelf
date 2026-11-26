import PropTypes from 'prop-types';
import { Component } from 'react';
import BookQuality from 'Book/BookQuality';
import FileDetailsModal from 'BookFile/FileDetailsModal';
import RecordingReviewModal from 'BookFile/RecordingReviewModal';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import VirtualTableSelectCell from 'Components/Table/Cells/VirtualTableSelectCell';
import { icons, kinds } from 'Helpers/Props';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import styles from './UnmappedFilesTableRow.css';

class UnmappedFilesTableRow extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isDetailsModalOpen: false,
      isInteractiveImportModalOpen: false,
      isRecordingReviewModalOpen: false,
      reviewSingleFile: false,
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

  onInteractiveImportPress = () => {
    this.setState({ isInteractiveImportModalOpen: true, reviewSingleFile: false });
  };

  onInteractiveImportModalClose = () => {
    this.setState({ isInteractiveImportModalOpen: false });
  };

  onRecordingReviewPress = () => {
    this.setState({ isRecordingReviewModalOpen: true });
  };

  onRecordingReviewClose = () => {
    this.setState({ isRecordingReviewModalOpen: false });
  };

  onRecordingReviewManualImport = () => {
    this.setState({
      isRecordingReviewModalOpen: false,
      isInteractiveImportModalOpen: true,
      reviewSingleFile: true
    });
  };

  onRecordingReviewCreateLocal = (fileId) => {
    this.onRecordingReviewClose();
    this.props.onCreateLocalRecording(fileId);
  };

  onAssociated = (review) => {
    this.onRecordingReviewClose();
    this.props.onRecordingAssociated(review);
  };

  onDeleteFilePress = () => {
    this.setState({ isConfirmDeleteModalOpen: true });
  };

  onConfirmDelete = () => {
    this.setState({ isConfirmDeleteModalOpen: false });
    this.props.deleteUnmappedFile(this.props.id);
  };

  onConfirmDeleteModalClose = () => {
    this.setState({ isConfirmDeleteModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      id,
      path,
      size,
      dateAdded,
      quality,
      columns,
      isSelected,
      onSelectedChange
    } = this.props;

    const folder = path.substring(0, Math.max(path.lastIndexOf('/'), path.lastIndexOf('\\')));

    const {
      isInteractiveImportModalOpen,
      isDetailsModalOpen,
      isRecordingReviewModalOpen,
      reviewSingleFile,
      isConfirmDeleteModalOpen
    } = this.state;

    return (
      <>
        {
          columns.map((column) => {
            const {
              name,
              isVisible
            } = column;

            if (!isVisible) {
              return null;
            }

            if (name === 'select') {
              return (
                <VirtualTableSelectCell
                  inputClassName={styles.checkInput}
                  id={id}
                  key={name}
                  isSelected={isSelected}
                  isDisabled={false}
                  onSelectedChange={onSelectedChange}
                />
              );
            }

            if (name === 'path') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {path}
                </VirtualTableRowCell>
              );
            }

            if (name === 'size') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  {formatBytes(size)}
                </VirtualTableRowCell>
              );
            }

            if (name === 'dateAdded') {
              return (
                <RelativeDateCellConnector
                  key={name}
                  className={styles[name]}
                  date={dateAdded}
                  component={VirtualTableRowCell}
                />
              );
            }

            if (name === 'quality') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  <BookQuality
                    quality={quality}
                  />
                </VirtualTableRowCell>
              );
            }

            if (name === 'actions') {
              return (
                <VirtualTableRowCell
                  key={name}
                  className={styles[name]}
                >
                  <IconButton
                    name={icons.SEARCH}
                    aria-label={translate('RecordingReviewAction')}
                    onPress={this.onRecordingReviewPress}
                  />

                  <IconButton
                    name={icons.INFO}
                    aria-label={translate('FileDetails')}
                    onPress={this.onDetailsPress}
                  />

                  <IconButton
                    name={icons.INTERACTIVE}
                    aria-label={translate('ManualImport')}
                    onPress={this.onInteractiveImportPress}
                  />

                  <IconButton
                    name={icons.DELETE}
                    aria-label={translate('DeleteBookFile')}
                    onPress={this.onDeleteFilePress}
                  />

                </VirtualTableRowCell>
              );
            }

            return null;
          })
        }

        <InteractiveImportModal
          isOpen={isInteractiveImportModalOpen}
          folder={reviewSingleFile ? path : folder}
          showFilterExistingFiles={true}
          filterExistingFiles={false}
          showImportMode={false}
          showReplaceExistingFiles={false}
          replaceExistingFiles={false}
          onModalClose={this.onInteractiveImportModalClose}
        />

        <FileDetailsModal
          isOpen={isDetailsModalOpen}
          onModalClose={this.onDetailsModalClose}
          id={id}
        />

        {
          isRecordingReviewModalOpen &&
            <RecordingReviewModal
              isOpen={true}
              fileId={id}
              onModalClose={this.onRecordingReviewClose}
              onManualImport={this.onRecordingReviewManualImport}
              onCreateLocalRecording={this.onRecordingReviewCreateLocal}
              onAssociated={this.onAssociated}
            />
        }

        <ConfirmModal
          isOpen={isConfirmDeleteModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteBookFile')}
          message={translate('DeleteBookFileMessageText', [path])}
          confirmLabel={translate('Delete')}
          onConfirm={this.onConfirmDelete}
          onCancel={this.onConfirmDeleteModalClose}
        />

      </>
    );
  }

}

UnmappedFilesTableRow.propTypes = {
  id: PropTypes.number.isRequired,
  path: PropTypes.string.isRequired,
  size: PropTypes.number.isRequired,
  quality: PropTypes.object.isRequired,
  dateAdded: PropTypes.string.isRequired,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  isSelected: PropTypes.bool,
  onSelectedChange: PropTypes.func.isRequired,
  onCreateLocalRecording: PropTypes.func.isRequired,
  onRecordingAssociated: PropTypes.func.isRequired,
  deleteUnmappedFile: PropTypes.func.isRequired
};

export default UnmappedFilesTableRow;
