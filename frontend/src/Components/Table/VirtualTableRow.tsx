import React from 'react';
import styles from './VirtualTableRow.css';

export type VirtualTableRowProps = React.HTMLAttributes<HTMLDivElement>;

export default function VirtualTableRow({ className = styles.row, children, ...otherProps }: VirtualTableRowProps) {
  return <div className={className} {...otherProps}>{children}</div>;
}
