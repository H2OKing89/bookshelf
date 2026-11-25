import React from 'react';
import styles from './TableRow.css';

export interface TableRowProps extends React.HTMLAttributes<HTMLTableRowElement> {
  overlayContent?: boolean;
}

export default function TableRow({ className = styles.row, children, ...otherProps }: TableRowProps) {
  delete otherProps.overlayContent;
  return <tr className={className} {...otherProps}>{children}</tr>;
}
