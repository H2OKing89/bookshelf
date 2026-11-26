import { Component } from 'react';
import type { AuthorId, BookId } from 'Book/BookIdentity';
import BookSearchCellConnector from 'Book/BookSearchCellConnector';
import BookTitleLink from 'Book/BookTitleLink';
import IndexerFlags from 'Book/IndexerFlags';
import type { LibraryBookFile } from 'Book/LibraryModels';
import Icon from 'Components/Icon';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import StarRating from 'Components/StarRating';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import type { TableSelectionChange } from 'Components/Table/tableSelection';
import Popover from 'Components/Tooltip/Popover';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import { getRecordingIdentity } from 'Utilities/Book/recordingIdentity';
import translate from 'Utilities/String/translate';
import BookStatus from './BookStatus';
import RecordingCatalogLabel from './RecordingCatalogLabel';
import styles from './BookRow.css';

export interface BookRowProps {
  id: BookId;
  authorId: AuthorId;
  monitored: boolean;
  releaseDate?: string | null;
  title: string;
  displayTitle?: string | null;
  foreignBookId?: string | null;
  foreignEditionId?: string | null;
  seriesTitle?: string | null;
  authorName: string;
  position?: string | number | null;
  pageCount?: number | null;
  runtimeLengthMinutes?: number | null;
  narrators?: readonly string[] | null;
  ratings?: { value?: number; votes?: number } | null;
  isSaving?: boolean;
  authorMonitored: boolean;
  titleSlug: string;
  bookFiles: readonly LibraryBookFile[];
  indexerFlags?: number;
  isEditorActive?: boolean;
  isSelected?: boolean;
  onSelectedChange: (change: TableSelectionChange<BookId>) => void;
  columns: readonly { name: string; isVisible?: boolean }[];
  onMonitorBookPress: (id: BookId, monitored: boolean, options: { shiftKey?: boolean }) => void;
}

class BookRow extends Component<BookRowProps, { isDetailsModalOpen: boolean; isEditBookModalOpen: boolean }> {

  //
  // Lifecycle

  constructor(props: BookRowProps) {
    super(props);

    this.state = {
      isDetailsModalOpen: false,
      isEditBookModalOpen: false
    };
  }

  //
  // Listeners

  onManualSearchPress = () => {
    this.setState({ isDetailsModalOpen: true });
  };

  onDetailsModalClose = () => {
    this.setState({ isDetailsModalOpen: false });
  };

  onEditBookPress = () => {
    this.setState({ isEditBookModalOpen: true });
  };

  onEditBookModalClose = () => {
    this.setState({ isEditBookModalOpen: false });
  };

  onMonitorBookPress = (monitored: boolean, options: { shiftKey?: boolean }) => {
    this.props.onMonitorBookPress(this.props.id, monitored, options);
  };

  //
  // Render

  render() {
    const {
      id,
      authorId,
      monitored,
      releaseDate,
      title,
      displayTitle,
      foreignBookId,
      foreignEditionId,
      seriesTitle,
      authorName,
      position,
      pageCount,
      runtimeLengthMinutes,
      narrators = [],
      ratings,
      isSaving,
      authorMonitored,
      titleSlug,
      bookFiles,
      indexerFlags = 0,
      isEditorActive,
      isSelected,
      onSelectedChange,
      columns
    } = this.props;

    const bookFile = bookFiles[0];
    const recording = getRecordingIdentity(foreignEditionId || foreignBookId);
    const isAvailable = releaseDate != null && Date.parse(releaseDate) < Date.now();

    return (
      <TableRow>
        {
          columns.map((column) => {
            const {
              name,
              isVisible
            } = column;

            if (!isVisible) {
              return null;
            }

            if (isEditorActive && name === 'select') {
              return (
                <TableSelectCell
                  key={name}
                  id={id}
                  isSelected={isSelected}
                  isDisabled={false}
                  onSelectedChange={onSelectedChange}
                />
              );
            }

            if (name === 'monitored') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.monitored}
                >
                  <MonitorToggleButton
                    aria-label={translate('MonitorBook')}
                    monitored={monitored}
                    isDisabled={!authorMonitored}
                    isSaving={isSaving}
                    onPress={this.onMonitorBookPress}
                  />
                </TableRowCell>
              );
            }

            if (name === 'title') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.title}
                >
                  <BookTitleLink
                    titleSlug={titleSlug}
                    title={title}
                    displayTitle={displayTitle}
                  />
                  <RecordingCatalogLabel recording={recording} />
                </TableRowCell>
              );
            }

            if (name === 'series') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.title}
                >
                  {seriesTitle || ''}
                </TableRowCell>
              );
            }

            if (name === 'position') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.position}
                >
                  {position || ''}
                </TableRowCell>
              );
            }

            if (name === 'rating') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.rating}
                >
                  {
                    <StarRating
                      rating={ratings?.value ?? 0}
                      votes={ratings?.votes ?? 0}
                    />
                  }
                </TableRowCell>
              );
            }

            if (name === 'releaseDate') {
              return (
                <RelativeDateCellConnector
                  className={styles.releaseDate}
                  key={name}
                  date={releaseDate}
                />
              );
            }

            if (name === 'runtimeLengthMinutes') {
              return (
                <TableRowCell key={name} className={styles.runtimeLengthMinutes}>
                  {runtimeLengthMinutes != null && runtimeLengthMinutes > 0 ? translate('RuntimeMinutes', { 0: runtimeLengthMinutes }) : ''}
                </TableRowCell>
              );
            }

            if (name === 'narrators') {
              const narratorNames = (narrators ?? []).join(', ');

              return (
                <TableRowCell key={name} className={styles.narrators}>
                  <span title={narratorNames}>{narratorNames}</span>
                </TableRowCell>
              );
            }

            if (name === 'pageCount') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.pageCount}
                >
                  {pageCount || ''}
                </TableRowCell>
              );
            }

            if (name === 'indexerFlags') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.indexerFlags}
                >
                  {indexerFlags ? (
                    <Popover
                      anchor={<Icon name={icons.FLAG} kind={kinds.PRIMARY} />}
                      title={translate('IndexerFlags')}
                      body={<IndexerFlags indexerFlags={indexerFlags} />}
                      position={tooltipPositions.LEFT}
                    />
                  ) : null}
                </TableRowCell>
              );
            }

            if (name === 'status') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.status}
                >
                  <BookStatus
                    isAvailable={isAvailable}
                    monitored={monitored}
                    bookFile={bookFile}
                    recordingAsin={recording?.asin}
                  />
                </TableRowCell>
              );
            }

            if (name === 'actions') {
              return (
                <BookSearchCellConnector
                  key={name}
                  bookId={id}
                  authorId={authorId}
                  bookTitle={title}
                  authorName={authorName}
                />
              );
            }
            return null;
          })
        }
      </TableRow>
    );
  }
}

export default BookRow;
