import type { AuthorStatus } from 'Author/Author';
import ProgressBar from 'Components/ProgressBar';
import { sizes } from 'Helpers/Props';
import getProgressBarKind from 'Utilities/Author/getProgressBarKind';
import translate from 'Utilities/String/translate';
import styles from './AuthorIndexProgressBar.css';

interface AuthorIndexProgressBarProps {
  monitored: boolean;
  status: AuthorStatus;
  bookCount: number;
  availableBookCount: number;
  bookFileCount: number;
  totalBookCount: number;
  posterWidth: number;
  detailedProgressBar: boolean;
}

function AuthorIndexProgressBar(props: AuthorIndexProgressBarProps) {
  const {
    monitored,
    status,
    bookCount,
    availableBookCount,
    bookFileCount,
    totalBookCount,
    posterWidth,
    detailedProgressBar
  } = props;

  const progress = bookCount ? (availableBookCount / bookCount) * 100 : 100;
  const text = `${availableBookCount} / ${bookCount}`;

  return (
    <ProgressBar
      className={styles.progressBar}
      containerClassName={styles.progress}
      progress={progress}
      kind={getProgressBarKind(status, monitored, progress)}
      size={detailedProgressBar ? sizes.MEDIUM : sizes.SMALL}
      showText={detailedProgressBar}
      text={text}
      title={translate('AuthorProgressBarText', { bookCount, availableBookCount, bookFileCount, totalBookCount })}
      width={posterWidth}
    />
  );
}

export default AuthorIndexProgressBar;
