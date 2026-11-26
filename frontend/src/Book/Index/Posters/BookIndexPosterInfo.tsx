import type { AuthorReference, NamedProfile } from 'Book/LibraryPresentation';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import formatBytes from 'Utilities/Number/formatBytes';
import { formatRecordingQualityRank } from 'Utilities/Quality/qualityRank';
import styles from './BookIndexPosterInfo.css';

interface BookIndexPosterInfoProps {
  qualityProfile?: NamedProfile | null;
  showQualityProfile: boolean;
  added?: string | null;
  sizeOnDisk?: number | null;
  sortKey: string;
  showRelativeDates: boolean;
  shortDateFormat: string;
  timeFormat: string;
  author: AuthorReference;
  releaseDate?: string | null;
  bookFileCount: number;
  fileQualityRank?: number | null;
}

function BookIndexPosterInfo(props: BookIndexPosterInfoProps) {
  const {
    qualityProfile,
    showQualityProfile,
    added,
    releaseDate,
    author,
    bookFileCount,
    sizeOnDisk,
    fileQualityRank,
    sortKey,
    showRelativeDates,
    shortDateFormat,
    timeFormat
  } = props;

  if (sortKey === 'qualityProfileId' && !showQualityProfile) {
    return (
      <div className={styles.info}>
        {qualityProfile?.name}
      </div>
    );
  }

  if (sortKey === 'added' && added) {
    const addedDate = getRelativeDate(
      added,
      shortDateFormat,
      showRelativeDates,
      {
        timeFormat,
        timeForToday: false
      }
    );

    return (
      <div className={styles.info}>
        {`Added ${addedDate}`}
      </div>
    );
  }

  if (sortKey === 'releaseDate' && added) {
    const date = getRelativeDate(
      releaseDate,
      shortDateFormat,
      showRelativeDates,
      {
        timeFormat,
        timeForToday: false
      }
    );

    return (
      <div className={styles.info}>
        {`Released ${date}`}
      </div>
    );
  }

  if (sortKey === 'bookFileCount') {
    let books = '1 file';

    if (bookFileCount === 0) {
      books = 'No files';
    } else if (bookFileCount > 1) {
      books = `${bookFileCount} files`;
    }

    return (
      <div className={styles.info}>
        {books}
      </div>
    );
  }

  if (sortKey === 'path') {
    return (
      <div className={styles.info}>
        {author.path}
      </div>
    );
  }

  if (sortKey === 'fileQualityRank') {
    return (
      <div className={styles.info} title="Lowest file quality rank">
        {formatRecordingQualityRank(fileQualityRank)}
      </div>
    );
  }

  if (sortKey === 'sizeOnDisk') {
    return (
      <div className={styles.info}>
        {formatBytes(sizeOnDisk)}
      </div>
    );
  }

  return null;
}

export default BookIndexPosterInfo;
