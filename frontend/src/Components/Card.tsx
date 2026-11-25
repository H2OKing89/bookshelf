import React from 'react';
import Link, { type LinkProps } from 'Components/Link/Link';
import styles from './Card.css';

export interface CardProps {
  className?: string;
  overlayClassName?: string;
  overlayContent?: boolean;
  children: React.ReactNode;
  'aria-label'?: string;
  onPress?: LinkProps['onPress'];
}

export default function Card({ className = styles.card, overlayClassName = styles.overlay, overlayContent = false, children, 'aria-label': ariaLabel, onPress }: CardProps) {
  if (overlayContent) {
    return (
      <div className={className}>
        <Link className={styles.underlay} aria-label={ariaLabel} onPress={onPress} />
        <div className={overlayClassName}>{children}</div>
      </div>
    );
  }
  return <Link className={className} aria-label={ariaLabel} onPress={onPress}>{children}</Link>;
}
