import Link, { type LinkProps } from 'Components/Link/Link';
import styles from './TableRowCellButton.css';

export default function TableRowCellButton({ className = styles.cell, ...otherProps }: LinkProps) {
  return <Link className={className} component="td" {...otherProps} />;
}
