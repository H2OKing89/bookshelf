import PropTypes from 'prop-types';
import ProtocolLabel from 'Activity/Queue/ProtocolLabel';
import BookFormats from 'Book/BookFormats';
import BookQuality from 'Book/BookQuality';
import formatChapterTime from 'Book/Details/formatChapterTime';
import IndexerFlags from 'Book/IndexerFlags';
import MediaInfo from 'BookFile/MediaInfo';
import { AUDIO } from 'BookFile/mediaInfoTypes';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import Popover from 'Components/Tooltip/Popover';
import Tooltip from 'Components/Tooltip/Tooltip';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import formatDateTime from 'Utilities/Date/formatDateTime';
import formatAge from 'Utilities/Number/formatAge';
import formatBytes from 'Utilities/Number/formatBytes';
import formatCustomFormatScore from 'Utilities/Number/formatCustomFormatScore';
import translate from 'Utilities/String/translate';
import Peers from './Peers';
import styles from './InteractiveSearchRow.css';

function getDownloadIcon(isGrabbing, isGrabbed, grabError) {
  if (isGrabbing) {
    return icons.SPINNER;
  } else if (isGrabbed || grabError) {
    return icons.DOWNLOADING;
  }

  return icons.DOWNLOAD;
}

function getDownloadKind(isGrabbed, grabError, downloadAllowed) {
  if (isGrabbed) {
    return kinds.SUCCESS;
  }

  return grabError || !downloadAllowed ? kinds.DANGER : kinds.DEFAULT;
}

function getDownloadTooltip(isGrabbing, isGrabbed, grabError) {
  if (isGrabbing) {
    return '';
  } else if (isGrabbed) {
    return 'Added to downloaded queue';
  } else if (grabError) {
    return grabError;
  }

  return 'Add to downloaded queue';
}

function RejectionReasons({ rejections, expanded }) {
  if (!rejections.length) {
    return null;
  }

  return (
    <details className={styles.reasons} open={expanded || undefined}>
      <summary>{translate('ReleaseRejected')} ({rejections.length})</summary>
      <ul>
        {rejections.map((rejection, index) => <li key={index}>{rejection}</li>)}
      </ul>
    </details>
  );
}

RejectionReasons.propTypes = {
  rejections: PropTypes.arrayOf(PropTypes.string).isRequired,
  expanded: PropTypes.bool
};

function InteractiveSearchRow({
  rowKey,
  index,
  total,
  card,
  protocol,
  age,
  ageHours,
  ageMinutes,
  publishDate,
  title,
  infoUrl,
  bookTitle,
  authorName,
  indexer,
  size,
  seeders,
  leechers,
  quality,
  mediaInfo,
  customFormatScore,
  customFormats = [],
  indexerFlags = 0,
  rejections = [],
  downloadAllowed,
  isGrabbing = false,
  isGrabbed = false,
  longDateFormat,
  timeFormat,
  grabError,
  canGrab,
  onGrabPress
}) {
  const downloadKind = getDownloadKind(isGrabbed, grabError, downloadAllowed);
  const downloadTooltip = getDownloadTooltip(isGrabbing, isGrabbed, grabError);
  const ageText = formatAge(age, ageHours, ageMinutes);
  const ageTitle = formatDateTime(publishDate, longDateFormat, timeFormat, { includeSeconds: true });
  const score = formatCustomFormatScore(customFormatScore, customFormats.length);
  const titleLink = infoUrl ? <Link to={infoUrl}>{title}</Link> : title;
  const audio = mediaInfo && <MediaInfo type={AUDIO} {...mediaInfo} />;
  const languages = [...new Set([mediaInfo?.language, ...(mediaInfo?.audioStreams ?? []).map((stream) => stream.language)].filter(Boolean))];
  const duration = mediaInfo?.durationSeconds;
  const parsedMetadata = [bookTitle, authorName].filter(Boolean).join(' · ');
  const grabStatus = grabError || (isGrabbed ? 'Added to downloaded queue' : undefined);
  const grabLabel = translate('GrabRelease');

  if (card) {
    return (
      <article className={styles.cardRow} role="listitem" aria-posinset={index + 1} aria-setsize={total} data-release-row={rowKey}>
        <div className={styles.card}>
          <div className={styles.cardHeading}>
            <ProtocolLabel protocol={protocol} />
            <span className={styles.cardAge} title={ageTitle}>{ageText}</span>
          </div>
          <div className={styles.cardTitle}>{titleLink}</div>
          {parsedMetadata && <div className={styles.parsedMetadata}>{translate('InteractiveSearchParsedMetadata')}: {parsedMetadata}</div>}
          <dl className={styles.cardFields}>
            <div><dt>{translate('Indexer')}</dt><dd>{indexer}</dd></div>
            <div><dt>{translate('Size').trim()}</dt><dd>{formatBytes(size)}</dd></div>
            <div><dt>{translate('Quality')}</dt><dd><BookQuality quality={quality} showRevision={true} /></dd></div>
            <div><dt>{translate('CustomFormatScore')}</dt><dd>{score || '0'} <BookFormats formats={customFormats} /></dd></div>
            {protocol === 'torrent' && <div><dt>Seeders / leechers</dt><dd><Peers seeders={seeders} leechers={leechers} /></dd></div>}
            {!!languages.length && <div><dt>{translate('Language')}</dt><dd>{languages.join(', ')}</dd></div>}
            {Number.isFinite(duration) && duration >= 0 && <div><dt>{translate('Duration')}</dt><dd>{formatChapterTime(duration * 1000)}</dd></div>}
          </dl>
          {audio}
          {
            !!indexerFlags &&
              <details className={styles.indexerDetails}>
                <summary>{translate('IndexerFlags')}</summary>
                <IndexerFlags indexerFlags={indexerFlags} />
              </details>
          }
          <RejectionReasons rejections={rejections} expanded={true} />
          <div className={styles.cardAction}>
            {grabStatus && <span role={grabError ? 'alert' : 'status'}>{grabStatus}</span>}
            <SpinnerButton
              kind={downloadKind}
              isSpinning={isGrabbing}
              isDisabled={!canGrab}
              title={downloadTooltip}
              aria-label={grabLabel}
              onPress={onGrabPress}
            >
              {grabLabel}
            </SpinnerButton>
          </div>
        </div>
      </article>
    );
  }

  return (
    <TableRow aria-rowindex={index + 2} data-release-row={rowKey}>
      <TableRowCell className={styles.protocol}>
        <ProtocolLabel protocol={protocol} />
      </TableRowCell>
      <TableRowCell className={styles.age} title={ageTitle}>{ageText}</TableRowCell>
      <TableRowCell>
        <div className={styles.titleContent}>{titleLink}</div>
        {audio}
        <RejectionReasons rejections={rejections} />
        {grabStatus && <div className={styles.grabStatus} role={grabError ? 'alert' : 'status'}>{grabStatus}</div>}
      </TableRowCell>
      <TableRowCell className={styles.indexer}>{indexer}</TableRowCell>
      <TableRowCell className={styles.size}>{formatBytes(size)}</TableRowCell>
      <TableRowCell className={styles.peers}>
        {protocol === 'torrent' && <Peers seeders={seeders} leechers={leechers} />}
      </TableRowCell>
      <TableRowCell className={styles.quality}>
        <BookQuality quality={quality} showRevision={true} />
      </TableRowCell>
      <TableRowCell className={styles.customFormatScore}>
        <Tooltip anchor={score} tooltip={<BookFormats formats={customFormats} />} position={tooltipPositions.LEFT} />
      </TableRowCell>
      <TableRowCell className={styles.indexerFlags}>
        {
          !!indexerFlags &&
            <Popover
              anchor={<Icon name={icons.FLAG} kind={kinds.PRIMARY} />}
              title={translate('IndexerFlags')}
              body={<IndexerFlags indexerFlags={indexerFlags} />}
              position={tooltipPositions.LEFT}
            />
        }
      </TableRowCell>
      <TableRowCell className={styles.rejected}>
        {!!rejections.length && <Icon name={icons.DANGER} kind={kinds.DANGER} aria-label={translate('ReleaseRejected')} />}
      </TableRowCell>
      <TableRowCell className={styles.download}>
        <SpinnerIconButton
          name={getDownloadIcon(isGrabbing, isGrabbed, grabError)}
          kind={downloadKind}
          title={downloadTooltip}
          isSpinning={isGrabbing}
          isDisabled={!canGrab}
          aria-label={grabLabel}
          onPress={onGrabPress}
        />
      </TableRowCell>
    </TableRow>
  );
}

InteractiveSearchRow.propTypes = {
  rowKey: PropTypes.string.isRequired,
  index: PropTypes.number.isRequired,
  total: PropTypes.number.isRequired,
  card: PropTypes.bool.isRequired,
  protocol: PropTypes.string.isRequired,
  age: PropTypes.number.isRequired,
  ageHours: PropTypes.number.isRequired,
  ageMinutes: PropTypes.number.isRequired,
  publishDate: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  infoUrl: PropTypes.string,
  bookTitle: PropTypes.string,
  authorName: PropTypes.string,
  indexer: PropTypes.string.isRequired,
  size: PropTypes.number.isRequired,
  seeders: PropTypes.number,
  leechers: PropTypes.number,
  quality: PropTypes.object.isRequired,
  mediaInfo: PropTypes.object,
  customFormats: PropTypes.arrayOf(PropTypes.object),
  customFormatScore: PropTypes.number.isRequired,
  indexerFlags: PropTypes.number,
  rejections: PropTypes.arrayOf(PropTypes.string),
  downloadAllowed: PropTypes.bool.isRequired,
  isGrabbing: PropTypes.bool,
  isGrabbed: PropTypes.bool,
  grabError: PropTypes.string,
  longDateFormat: PropTypes.string.isRequired,
  timeFormat: PropTypes.string.isRequired,
  canGrab: PropTypes.bool.isRequired,
  onGrabPress: PropTypes.func.isRequired
};

export default InteractiveSearchRow;
