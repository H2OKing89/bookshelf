import React from 'react';
import styles from './ModalFooter.css';

export default function ModalFooter({ children, ...otherProps }: React.HTMLAttributes<HTMLDivElement>) {
  return <div className={styles.modalFooter} {...otherProps}>{children}</div>;
}
