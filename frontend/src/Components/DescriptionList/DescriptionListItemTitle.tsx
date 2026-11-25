import React from 'react';
import styles from './DescriptionListItemTitle.css';

export interface DescriptionListItemTitleProps {
  className?: string;
  children?: React.ReactNode;
}

export default function DescriptionListItemTitle({ className = styles.title, children }: DescriptionListItemTitleProps) {
  return <dt className={className}>{children}</dt>;
}
