import classNames from 'classnames';
import React from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import styles from './FormInputHelpText.css';

export interface FormInputHelpTextProps {
  className?: string;
  id?: string;
  text: React.ReactNode;
  link?: string;
  tooltip?: string;
  isError?: boolean;
  isWarning?: boolean;
  isCheckInput?: boolean;
}

function FormInputHelpText(props: FormInputHelpTextProps) {
  const {
    className = styles.helpText,
    id,
    text,
    link,
    tooltip,
    isError = false,
    isWarning = false,
    isCheckInput = false
  } = props;

  return (
    <div
      id={id}
      className={classNames(
        className,
        isError && styles.isError,
        isWarning && styles.isWarning,
        isCheckInput && styles.isCheckInput
      )}
    >
      {text}

      {
        link ?
          <Link
            className={styles.link}
            to={link}
            title={tooltip}
          >
            <Icon
              name={icons.EXTERNAL_LINK}
            />
          </Link> :
          null
      }

      {
        !link && tooltip ?
          <Icon
            containerClassName={styles.details}
            name={icons.INFO}
            title={tooltip}
          /> :
          null
      }
    </div>
  );
}

export default FormInputHelpText;
