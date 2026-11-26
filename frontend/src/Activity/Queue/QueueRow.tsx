import { Component } from 'react';
import ProtocolLabel from 'Activity/Queue/ProtocolLabel';
import AuthorNameLink from 'Author/AuthorNameLink';
import BookFormats from 'Book/BookFormats';
import type { DownloadClientId } from 'Book/BookIdentity';
import BookQuality from 'Book/BookQuality';
import BookTitleLink from 'Book/BookTitleLink';
import Icon from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import ProgressBar from 'Components/ProgressBar';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import Popover from 'Components/Tooltip/Popover';
import Tooltip from 'Components/Tooltip/Tooltip';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';
import formatBytes from 'Utilities/Number/formatBytes';
import formatCustomFormatScore from 'Utilities/Number/formatCustomFormatScore';
import translate from 'Utilities/String/translate';
import type { QueueColumn } from './queueColumns';
import { holdQueueRefresh,type QueueItem, type QueueRowId } from './queuePageQuery';
import QueueStatusCell from './QueueStatusCell';
import TimeleftCell from './TimeleftCell';
import styles from './QueueRow.css';

interface QueueRowProps extends QueueItem {
  columns: readonly QueueColumn[];
  showRelativeDates: boolean;
  shortDateFormat: string;
  timeFormat: string;
  isGrabbing?: boolean;
  isRemoving?: boolean;
  isSelected?: boolean;
  canUseRowActions: boolean;
  onSelectedChange(change: { id: QueueRowId; value: boolean | null | undefined; shiftKey?: boolean }): void;
  onGrabPress(id: QueueRowId): void;
  onRemoveQueueItemPress(id: QueueRowId): void;
}

interface QueueRowState {
  interactiveImport: { downloadId: string; downloadClientId: DownloadClientId; title: string } | null;
}

class QueueRow extends Component<QueueRowProps, QueueRowState> {
  state: QueueRowState = { interactiveImport: null };
  componentWillUnmount() {
    this.releaseImportHold?.();
  }

  private releaseImportHold?: () => void;

  onInteractiveImportPress = () => {
    const { canUseRowActions, canImport, downloadId, downloadClientId, title } = this.props;

    if (canUseRowActions && canImport && downloadId && downloadClientId && !this.releaseImportHold) {
      this.releaseImportHold = holdQueueRefresh();
      this.setState({ interactiveImport: { downloadId, downloadClientId, title } });
    }
  };

  onInteractiveImportModalClose = () => {
    this.releaseImportHold?.();
    this.releaseImportHold = undefined;
    this.setState({ interactiveImport: null });
  };

  //
  // Render

  render() {
    const {
      id,
      downloadClientAvailable,
      title,
      status,
      trackedDownloadStatus,
      trackedDownloadState,
      statusMessages,
      errorMessage,
      author,
      recordings,
      coverageSource,
      canRemove,
      canGrab,
      canImport,
      quality,
      customFormats,
      customFormatScore,
      protocol,
      indexer,
      outputPath,
      downloadClient,
      downloadForced,
      estimatedCompletionTime,
      timeleft,
      size,
      sizeleft,
      showRelativeDates,
      shortDateFormat,
      timeFormat,
      isGrabbing = false,
      isRemoving = false,
      isSelected = false,
      columns,
      canUseRowActions,
      onSelectedChange,
      onGrabPress,
      onRemoveQueueItemPress
    } = this.props;

    const { interactiveImport } = this.state;

    const progress = size > 0 ? Math.min(100, Math.max(0, 100 - sizeleft / size * 100)) : 0;
    const earliestReleaseDate = recordings.map(({ releaseDate }) => releaseDate).filter((date): date is string => !!date).sort()[0];

    return (
      <TableRow>
        <TableSelectCell
          id={id}
          isSelected={isSelected}
          isDisabled={!canUseRowActions || !(canRemove || canGrab)}
          onSelectedChange={onSelectedChange}
        />

        {
          columns.map((column) => {
            const {
              name,
              isVisible
            } = column;

            if (!isVisible) {
              return null;
            }

            if (name === 'status') {
              return (
                <QueueStatusCell
                  key={name}
                  sourceTitle={title}
                  status={status}
                  trackedDownloadStatus={trackedDownloadStatus}
                  trackedDownloadState={trackedDownloadState}
                  downloadClientAvailable={downloadClientAvailable}
                  statusMessages={statusMessages}
                  errorMessage={errorMessage}
                />
              );
            }

            if (name === 'authorMetadata.sortName') {
              return (
                <TableRowCell key={name}>
                  {
                    author?.titleSlug ?
                      <AuthorNameLink
                        titleSlug={author.titleSlug}
                        authorName={author.authorName}
                      /> :
                      author?.authorName || title
                  }
                </TableRowCell>
              );
            }

            if (name === 'books.title') {
              return (
                <TableRowCell key={name}>
                  {
                    recordings.map((recording) => (
                      <div key={recording.bookId}>
                        {
                          recording.titleSlug && recording.title ?
                            <BookTitleLink titleSlug={recording.titleSlug} title={recording.title} /> :
                            recording.title || translate('QueueRecordingMissing', { bookId: recording.bookId })
                        }
                        {
                          recording.recordingId &&
                            <small className={styles.recordingId}>{recording.recordingId}</small>
                        }
                      </div>
                    ))
                  }
                  {
                    coverageSource === 'mappedCandidates' &&
                      <small>{translate('QueueMappedCandidates')}</small>
                  }
                  {
                    coverageSource === 'unknown' &&
                      <small>{translate('QueueUnresolvedRecording')}</small>
                  }
                </TableRowCell>
              );
            }

            if (name === 'books.releaseDate') {
              return earliestReleaseDate ? (
                <RelativeDateCellConnector key={name} date={earliestReleaseDate} />
              ) : (
                <TableRowCell key={name}>-</TableRowCell>
              );
            }

            if (name === 'quality') {
              return (
                <TableRowCell key={name}>
                  <BookQuality
                    quality={quality}
                  />
                </TableRowCell>
              );
            }

            if (name === 'customFormats') {
              return (
                <TableRowCell key={name}>
                  <BookFormats
                    formats={customFormats}
                  />
                </TableRowCell>
              );
            }

            if (name === 'customFormatScore') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.customFormatScore}
                >
                  <Tooltip
                    anchor={formatCustomFormatScore(
                      customFormatScore,
                      customFormats.length
                    )}
                    tooltip={<BookFormats formats={customFormats} />}
                    position={tooltipPositions.BOTTOM}
                  />
                </TableRowCell>
              );
            }

            if (name === 'protocol') {
              return (
                <TableRowCell key={name}>
                  <ProtocolLabel
                    protocol={protocol}
                  />
                </TableRowCell>
              );
            }

            if (name === 'indexer') {
              return (
                <TableRowCell key={name}>
                  {indexer}
                </TableRowCell>
              );
            }

            if (name === 'downloadClient') {
              return (
                <TableRowCell key={name}>
                  {downloadClient}{downloadClientAvailable === false ? ' (unavailable)' : ''}
                </TableRowCell>
              );
            }

            if (name === 'title') {
              return (
                <TableRowCell key={name}>
                  {title}
                </TableRowCell>
              );
            }

            if (name === 'size') {
              return (
                <TableRowCell key={name}>{formatBytes(size)}</TableRowCell>
              );
            }

            if (name === 'outputPath') {
              return (
                <TableRowCell key={name}>
                  {outputPath}
                </TableRowCell>
              );
            }

            if (name === 'estimatedCompletionTime') {
              return (
                <TimeleftCell
                  key={name}
                  status={status}
                  estimatedCompletionTime={estimatedCompletionTime}
                  timeleft={timeleft}
                  size={size}
                  sizeleft={sizeleft}
                  showRelativeDates={showRelativeDates}
                  shortDateFormat={shortDateFormat}
                  timeFormat={timeFormat}
                />
              );
            }

            if (name === 'progress') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.progress}
                >
                  {
                    !!progress &&
                      <ProgressBar
                        progress={progress}
                        title={`${progress.toFixed(1)}%`}
                      />
                  }
                </TableRowCell>
              );
            }

            if (name === 'actions') {
              return (
                <TableRowCell
                  key={name}
                  className={styles.actions}
                >
                  {
                    downloadForced &&
                      <Popover
                        anchor={
                          <Icon
                            name={icons.DANGER}
                            kind={kinds.DANGER}
                          />
                        }
                        title={translate('ManualDownload')}
                        body="This release failed parsing checks and was manually downloaded from an interactive search.  Import is likely to fail."
                        position={tooltipPositions.LEFT}
                      />
                  }

                  {
                    canImport &&
                      <IconButton
                        name={icons.INTERACTIVE}
                        aria-label={translate('ManualImport')}
                        isDisabled={!canUseRowActions}
                        onPress={this.onInteractiveImportPress}
                      />
                  }

                  {
                    canGrab &&
                      <SpinnerIconButton
                        name={icons.DOWNLOAD}
                        isSpinning={isGrabbing}
                        isDisabled={!canUseRowActions}
                        aria-label={translate('GrabRelease')}
                        onPress={() => onGrabPress(id)}
                      />
                  }

                  <SpinnerIconButton
                    title={translate('RemoveFromQueue')}
                    name={icons.REMOVE}
                    isSpinning={isRemoving}
                    isDisabled={!canUseRowActions || !canRemove}
                    onPress={() => onRemoveQueueItemPress(id)}
                  />
                </TableRowCell>
              );
            }

            return null;
          })
        }

        <InteractiveImportModal
          isOpen={!!interactiveImport}
          {...interactiveImport}
          onModalClose={this.onInteractiveImportModalClose}
          showReplaceExistingFiles={true}
          replaceExistingFiles={true}
        />
      </TableRow>
    );
  }

}

export default QueueRow;
