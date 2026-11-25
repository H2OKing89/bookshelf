import React from 'react';
import styles from './TableRowCell.css';

export type TableRowCellProps = React.TdHTMLAttributes<HTMLTableCellElement>;

export default function TableRowCell({ className = styles.cell, children, ...otherProps }: TableRowCellProps) {
  return <td className={className} {...otherProps}>{children}</td>;
}
