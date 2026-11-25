import classNames from 'classnames';
import React from 'react';
import { kinds, sizes } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import type { Size } from 'Helpers/Props/sizes';
import styles from './Label.css';

const styleClasses: Partial<Record<Kind | Size, string>> = styles;
export interface LabelProps extends React.HTMLAttributes<HTMLSpanElement> {
  kind?: Kind;
  size?: Size;
  outline?: boolean;
  children: React.ReactNode;
}

export default function Label({ className = styles.label, kind = kinds.DEFAULT, size = sizes.SMALL, outline = false, children, ...otherProps }: LabelProps) {
  return (
    <span className={classNames(className, styleClasses[kind], styleClasses[size], outline && styles.outline)} {...otherProps}>
      {children}
    </span>
  );
}
