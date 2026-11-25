import classNames from 'classnames';
import React from 'react';
import { kinds } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import styles from './Alert.css';

const kindClasses: Partial<Record<Kind, string>> = styles;
export interface AlertProps extends React.HTMLAttributes<HTMLDivElement> {
  kind?: Kind;
  children: React.ReactNode;
}

export default function Alert({ className = styles.alert, kind = kinds.INFO, children, ...otherProps }: AlertProps) {
  return <div className={classNames(className, kindClasses[kind])} {...otherProps}>{children}</div>;
}
