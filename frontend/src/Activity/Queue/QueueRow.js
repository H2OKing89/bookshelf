import PropTypes from 'prop-types';
import React, { Component } from 'react';
import ProtocolLabel from 'Activity/Queue/ProtocolLabel';
import AuthorNameLink from 'Author/AuthorNameLink';
import BookFormats from 'Book/BookFormats';
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
import { holdQueueRefresh } from './queuePageQuery';
import QueueStatusCell from './QueueStatusCell';
import TimeleftCell from './TimeleftCell';
import styles from './QueueRow.css';

class QueueRow extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = { interactiveImport: null };
  }

  componentWillUnmount() {
    this.releaseImportHold?.();
  }

  onInteractiveImportPress = () => {
    const { canUseRowActions, canImport, downloadId, downloadClientId, title } = this.props;

    if (canUseRowActions && canImport && !this.releaseImportHold) {
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
      isGrabbing,
      isRemoving,
      isSelected,
      columns,
      canUseRowActions,
      onSelectedChange,
      onGrabPress,
      onRemoveQueueItemPress
    } = this.props;

    const { interactiveImport } = this.state;

    const progress = size > 0 ? Math.min(100, Math.max(0, 100 - sizeleft / size * 100)) : 0;
    const earliestReleaseDate = recordings.map(({ releaseDate }) => releaseDate).filter(Boolean).sort()[0];

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

QueueRow.propTypes = {
  id: PropTypes.string.isRequired,
  downloadId: PropTypes.string,
  downloadClientId: PropTypes.number,
  downloadClientAvailable: PropTypes.bool,
  title: PropTypes.string.isRequired,
  status: PropTypes.string.isRequired,
  trackedDownloadStatus: PropTypes.string,
  trackedDownloadState: PropTypes.string,
  statusMessages: PropTypes.arrayOf(PropTypes.object),
  errorMessage: PropTypes.string,
  author: PropTypes.object,
  recordings: PropTypes.arrayOf(PropTypes.object).isRequired,
  coverageSource: PropTypes.string.isRequired,
  canRemove: PropTypes.bool.isRequired,
  canGrab: PropTypes.bool.isRequired,
  canImport: PropTypes.bool.isRequired,
  quality: PropTypes.object.isRequired,
  customFormats: PropTypes.arrayOf(PropTypes.object),
  customFormatScore: PropTypes.number.isRequired,
  protocol: PropTypes.string.isRequired,
  indexer: PropTypes.string,
  outputPath: PropTypes.string,
  downloadClient: PropTypes.string,
  downloadForced: PropTypes.bool.isRequired,
  estimatedCompletionTime: PropTypes.string,
  timeleft: PropTypes.string,
  size: PropTypes.number,
  sizeleft: PropTypes.number,
  showRelativeDates: PropTypes.bool.isRequired,
  shortDateFormat: PropTypes.string.isRequired,
  timeFormat: PropTypes.string.isRequired,
  isGrabbing: PropTypes.bool.isRequired,
  isRemoving: PropTypes.bool.isRequired,
  isSelected: PropTypes.bool,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  onSelectedChange: PropTypes.func.isRequired,
  onGrabPress: PropTypes.func.isRequired,
  onRemoveQueueItemPress: PropTypes.func.isRequired,
  canUseRowActions: PropTypes.bool.isRequired
};

QueueRow.defaultProps = {
  customFormats: [],
  isGrabbing: false,
  isRemoving: false
};

export default QueueRow;
