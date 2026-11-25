import React from 'react';
import styles from './VirtualTableRowCell.css';

export type VirtualTableRowCellProps = React.HTMLAttributes<HTMLDivElement>;

export default function VirtualTableRowCell({ className = styles.cell, children, ...otherProps }: VirtualTableRowCellProps) {
  return <div className={className} {...otherProps}>{children}</div>;
}
