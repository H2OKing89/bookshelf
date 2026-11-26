import ProgressBar from 'Components/ProgressBar';
import { sizes } from 'Helpers/Props';
import getProgressBarKind from 'Utilities/Author/getProgressBarKind';
import translate from 'Utilities/String/translate';
import styles from './BookIndexProgressBar.css';

interface BookIndexProgressBarProps {
  monitored: boolean;
  bookCount: number;
  bookFileCount: number;
  totalBookCount: number;
  posterWidth: number;
  detailedProgressBar: boolean;
}

function BookIndexProgressBar(props: BookIndexProgressBarProps) {
  const {
    monitored,
    bookCount,
    bookFileCount,
    totalBookCount,
    posterWidth,
    detailedProgressBar
  } = props;

  const progress = bookFileCount && bookCount ? (totalBookCount / bookCount) * 100 : 0;
  const text = `${bookFileCount ? bookCount : 0} / ${totalBookCount}`;

  return (
    <ProgressBar
      className={styles.progressBar}
      containerClassName={styles.progress}
      progress={100}
      kind={getProgressBarKind('ended', monitored, progress)}
      size={detailedProgressBar ? sizes.MEDIUM : sizes.SMALL}
      showText={detailedProgressBar}
      text={text}
      title={translate('BookProgressBarText', {
        bookCount: bookFileCount ? bookCount : 0,
        bookFileCount,
        totalBookCount
      })}
      width={posterWidth}
    />
  );
}

export default BookIndexProgressBar;
