import React from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './ModalContent.css';

export interface ModalContentProps extends React.HTMLAttributes<HTMLDivElement> {
  showCloseButton?: boolean;
  onModalClose: () => void;
}
function ModalContent(props: ModalContentProps) {
  const {
    className = styles.modalContent,
    children,
    showCloseButton = true,
    onModalClose,
    ...otherProps
  } = props;

  return (
    <div
      className={className}
      {...otherProps}
    >
      {
        showCloseButton &&
          <Link
            className={styles.closeButton}
            aria-label={translate('Close')}
            onPress={onModalClose}
          >
            <Icon
              name={icons.CLOSE}
              size={18}
              aria-hidden={true}
            />
          </Link>
      }

      {children}
    </div>
  );
}

export default ModalContent;
