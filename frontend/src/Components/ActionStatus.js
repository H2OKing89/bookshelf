import PropTypes from 'prop-types';
import React, { useEffect, useState } from 'react';
import translate from 'Utilities/String/translate';
import styles from './ActionStatus.css';

const settingsSavedEvent = 'bookshelf:settings-saved';

export function announceSettingsSaved() {
  document.dispatchEvent(new Event(settingsSavedEvent));
}

function ActionStatus({ message, isError, sequence }) {
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

ActionStatus.propTypes = {
  message: PropTypes.string,
  isError: PropTypes.bool,
  sequence: PropTypes.number
};

export function SavedSettingsStatus() {
  const [sequence, setSequence] = useState(0);

  useEffect(() => {
    let timeout = null;

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
