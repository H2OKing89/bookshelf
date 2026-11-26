import type { BookReference, NamedProfile } from 'Book/LibraryPresentation';
import getRelativeDate from 'Utilities/Date/getRelativeDate';
import formatBytes from 'Utilities/Number/formatBytes';
import styles from './AuthorIndexPosterInfo.css';

interface AuthorIndexPosterInfoProps {
  qualityProfile?: NamedProfile | null;
  showQualityProfile: boolean;
  added?: string | null;
  sizeOnDisk?: number | null;
  sortKey: string;
  showRelativeDates: boolean;
  shortDateFormat: string;
  timeFormat: string;
  metadataProfile?: NamedProfile | null;
  nextBook?: BookReference | null;
  lastBook?: BookReference | null;
  bookCount: number;
  path: string;
}

function AuthorIndexPosterInfo(props: AuthorIndexPosterInfoProps) {
  const {
    qualityProfile,
    showQualityProfile,
    metadataProfile,
    added,
    nextBook,
    lastBook,
    bookCount,
    path,
    sizeOnDisk,
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

  if (sortKey === 'metadataProfileId') {
    return (
      <div className={styles.info}>
        {metadataProfile?.name}
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

  if (sortKey === 'nextBook' && nextBook) {
    const date = getRelativeDate(
      nextBook.releaseDate,
      shortDateFormat,
      showRelativeDates,
      {
        timeFormat,
        timeForToday: false
      }
    );

    return (
      <div className={styles.info}>
        {`Next Book ${date}`}
      </div>
    );
  }

  if (sortKey === 'lastBook' && lastBook) {
    const date = getRelativeDate(
      lastBook.releaseDate,
      shortDateFormat,
      showRelativeDates,
      {
        timeFormat,
        timeForToday: false
      }
    );

    return (
      <div className={styles.info}>
        {`Last Book ${date}`}
      </div>
    );
  }

  if (sortKey === 'bookCount') {
    let books = '1 book';

    if (bookCount === 0) {
      books = 'No books';
    } else if (bookCount > 1) {
      books = `${bookCount} books`;
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
        {path}
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

export default AuthorIndexPosterInfo;
