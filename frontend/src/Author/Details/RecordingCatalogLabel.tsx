import React from 'react';
import { getRecordingIdentity } from 'Utilities/Book/recordingIdentity';
import translate from 'Utilities/String/translate';
import styles from './BookRow.css';

interface RecordingCatalogLabelProps {
  recording: ReturnType<typeof getRecordingIdentity>;
}

function RecordingCatalogLabel({ recording }: RecordingCatalogLabelProps) {
  if (!recording) {
    return null;
  }

  return (
    <div className={styles.recordingIdentity}>
      ASIN {recording.asin} · {translate('RecordingCatalogLabel', { marketplace: recording.marketplace })}
    </div>
  );
}

export default RecordingCatalogLabel;
