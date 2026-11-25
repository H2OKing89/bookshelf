import React from 'react';
import Link, { type LinkProps } from 'Components/Link/Link';
import styles from './TableRowButton.css';

export default function TableRowButton({ className = styles.row, ...otherProps }: LinkProps) {
  return <Link className={className} component="tr" {...otherProps} />;
}
