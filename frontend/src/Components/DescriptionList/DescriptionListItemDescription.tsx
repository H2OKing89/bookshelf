import React from 'react';
import styles from './DescriptionListItemDescription.css';

export interface DescriptionListItemDescriptionProps {
  className?: string;
  children?: React.ReactNode;
}

export default function DescriptionListItemDescription({ className = styles.description, children }: DescriptionListItemDescriptionProps) {
  return <dd className={className}>{children}</dd>;
}
