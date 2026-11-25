import React, { useEffect, useState } from 'react';
import translate from 'Utilities/String/translate';
import styles from './ActionStatus.css';

const settingsSavedEvent = 'bookshelf:settings-saved';

export function announceSettingsSaved() {
  document.dispatchEvent(new Event(settingsSavedEvent));
}

export interface ActionStatusProps {
  message?: string;
  isError?: boolean;
  sequence?: number;
}

function ActionStatus({ message, isError, sequence }: ActionStatusProps) {
  return (
    <>
      <span className={styles.visuallyHidden} role="status" aria-atomic={true}>
        {message && !isError && <span key={sequence}>{message}</span>}
      </span>
      <span className={styles.visuallyHidden} role="alert" aria-atomic={true}>
        {message && isError && <span key={sequence}>{message}</span>}
      </span>
    </>
  );
}

export function SavedSettingsStatus() {
  const [sequence, setSequence] = useState(0);

  useEffect(() => {
    let timeout: ReturnType<typeof setTimeout> | undefined = undefined;

    function onSettingsSaved() {
      clearTimeout(timeout);
      setSequence((previous) => previous + 1);
      timeout = setTimeout(() => setSequence(0), 3000);
    }

    document.addEventListener(settingsSavedEvent, onSettingsSaved);
    return () => {
      document.removeEventListener(settingsSavedEvent, onSettingsSaved);
      clearTimeout(timeout);
    };
  }, []);

  return <ActionStatus sequence={sequence} message={sequence ? translate('SettingsSaved') : undefined} />;
}

export default ActionStatus;
