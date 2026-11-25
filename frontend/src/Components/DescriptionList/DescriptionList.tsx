import React from 'react';
import styles from './DescriptionList.css';

export interface DescriptionListProps {
  className?: string;
  children?: React.ReactNode;
}

export default function DescriptionList({ className = styles.descriptionList, children }: DescriptionListProps) {
  return <dl className={className}>{children}</dl>;
}
