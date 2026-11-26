import { Component } from 'react';
import HistoryDetailsConnector from 'Activity/History/Details/HistoryDetailsConnector';
import HistoryEventTypeCell from 'Activity/History/HistoryEventTypeCell';
import type Author from 'Author/Author';
import BookFormats from 'Book/BookFormats';
import BookQuality from 'Book/BookQuality';
import type { LibraryBook } from 'Book/LibraryModels';
import Icon from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import Popover from 'Components/Tooltip/Popover';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import formatCustomFormatScore from 'Utilities/Number/formatCustomFormatScore';
import translate from 'Utilities/String/translate';
import type { AuthorHistoryItem } from './authorHistoryState';
import styles from './AuthorHistoryRow.css';

function getTitle(eventType: string) {
  switch (eventType) {
    case 'grabbed':
      return 'Grabbed';
    case 'downloadImported':
      return 'Download Completed';
    case 'bookFileImported':
      return 'Book Imported';
    case 'downloadFailed':
      return 'Download Failed';
    case 'bookFileDeleted':
      return 'Book File Deleted';
    case 'bookFileRenamed':
      return 'Book File Renamed';
    case 'bookFileRetagged':
      return 'Book File Tags Updated';
    case 'bookImportIncomplete':
      return 'Book Import Incomplete';
    default:
      return 'Unknown';
  }
}

interface Props extends AuthorHistoryItem {
  fullAuthor: boolean;
  author?: Author;
  book?: LibraryBook;
  onMarkAsFailedPress: (id: number) => void;
}

class AuthorHistoryRow extends Component<Props, { isMarkAsFailedModalOpen: boolean }> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      isMarkAsFailedModalOpen: false
    };
  }

  //
  // Listeners

  onMarkAsFailedPress = () => {
    this.setState({ isMarkAsFailedModalOpen: true });
  };

  onConfirmMarkAsFailed = () => {
    this.props.onMarkAsFailedPress(this.props.id);
    this.setState({ isMarkAsFailedModalOpen: false });
  };

  onMarkAsFailedModalClose = () => {
    this.setState({ isMarkAsFailedModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      eventType,
      sourceTitle,
      quality,
      qualityCutoffNotMet,
      customFormats,
      customFormatScore,
      date,
      data,
      book
    } = this.props;

    const {
      isMarkAsFailedModalOpen
    } = this.state;

    return (
      <TableRow>
        <HistoryEventTypeCell
          eventType={eventType}
          data={data}
        />

        <TableRowCell>
          {book?.title ?? translate('Unknown')}
        </TableRowCell>

        <TableRowCell className={styles.sourceTitle}>
          {sourceTitle}
        </TableRowCell>

        <TableRowCell>
          <BookQuality
            quality={quality}
            isCutoffNotMet={qualityCutoffNotMet}
          />
        </TableRowCell>

        <TableRowCell>
          <BookFormats formats={customFormats} />
        </TableRowCell>

        <TableRowCell>
          {formatCustomFormatScore(customFormatScore, customFormats.length)}
        </TableRowCell>

        <RelativeDateCellConnector
          date={date}
        />

        <TableRowCell className={styles.actions}>
          <Popover
            anchor={
              <Icon
                name={icons.INFO}
              />
            }
            title={getTitle(eventType)}
            body={
              <HistoryDetailsConnector
                eventType={eventType}
                sourceTitle={sourceTitle}
                data={data}
              />
            }
            position={tooltipPositions.LEFT}
          />

          {
            eventType === 'grabbed' &&
              <IconButton
                title={translate('MarkAsFailed')}
                name={icons.REMOVE}
                size={14}
                onPress={this.onMarkAsFailedPress}
              />
          }
        </TableRowCell>

        <ConfirmModal
          isOpen={isMarkAsFailedModalOpen}
          kind={kinds.DANGER}
          title={translate('MarkAsFailed')}
          message={translate('MarkAsFailedMessageText', { 0: sourceTitle })}
          confirmLabel={translate('MarkAsFailed')}
          onConfirm={this.onConfirmMarkAsFailed}
          onCancel={this.onMarkAsFailedModalClose}
        />
      </TableRow>
    );
  }
}

export default AuthorHistoryRow;
