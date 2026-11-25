import classNames from 'classnames';
import React from 'react';
import { kinds, sizes } from 'Helpers/Props';
import type { Alignment } from 'Helpers/Props/align';
import type { Kind } from 'Helpers/Props/kinds';
import type { Size } from 'Helpers/Props/sizes';
import Link, { type LinkProps } from './Link';
import styles from './Button.css';

const styleClasses: Partial<Record<Kind | Size | Alignment, string>> = styles;
export interface ButtonProps extends Omit<LinkProps, 'size' | 'kind'> {
  buttonGroupPosition?: Alignment;
  kind?: Kind;
  size?: Size;
}

export default function Button({ className = styles.button, buttonGroupPosition, kind = kinds.DEFAULT, size = sizes.MEDIUM, children, ...otherProps }: ButtonProps) {
  return (
    <Link className={classNames(className, styleClasses[kind], styleClasses[size], buttonGroupPosition && styleClasses[buttonGroupPosition])} {...otherProps}>
      {children}
    </Link>
  );
}
