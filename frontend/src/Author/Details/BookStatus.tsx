import BookQuality from 'Book/BookQuality';
import type { LibraryBookFile } from 'Book/LibraryModels';
import Label from 'Components/Label';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './BookStatus.css';

interface Props {
  isAvailable?: boolean;
  monitored: boolean;
  bookFile?: LibraryBookFile | null;
  recordingAsin?: string | null;
}

function BookStatus(props: Props) {
  const {
    isAvailable,
    monitored,
    bookFile,
    recordingAsin
  } = props;

  const hasBookFile = !!bookFile;

  if (hasBookFile) {
    const quality = bookFile.quality;

    return (
      <div className={styles.center}>
        <BookQuality
          title={quality?.quality.name}
          size={bookFile.size}
          quality={quality}
          isMonitored={monitored}
          isCutoffNotMet={bookFile.qualityCutoffNotMet === true}
        />
      </div>
    );
  }

  if (!monitored) {
    return (
      <div className={styles.center}>
        <Label
          title={translate('NotMonitored')}
          kind={kinds.WARNING}
        >
          {translate('NotMonitored')}
        </Label>
      </div>
    );
  }

  if (isAvailable) {
    return (
      <div className={styles.center}>
        <Label
          title={recordingAsin ? translate('RecordingAvailableButMissing', { asin: recordingAsin }) : translate('BookAvailableButMissing')}
          kind={kinds.DANGER}
        >
          {translate(recordingAsin ? 'RecordingMissing' : 'Missing')}
        </Label>
      </div>
    );
  }

  return (
    <div className={styles.center}>
      <Label
        title={translate('NotAvailable')}
        kind={kinds.INFO}
      >
        {translate('NotAvailable')}
      </Label>
    </div>
  );
}

export default BookStatus;
