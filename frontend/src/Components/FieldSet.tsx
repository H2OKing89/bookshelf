import classNames from 'classnames';
import React from 'react';
import { sizes } from 'Helpers/Props';
import type { Size } from 'Helpers/Props/sizes';
import styles from './FieldSet.css';

export interface FieldSetProps {
  size?: Size;
  legend?: React.ReactNode;
  children?: React.ReactNode;
}

export default function FieldSet({ size = sizes.MEDIUM, legend, children }: FieldSetProps) {
  return (
    <fieldset className={styles.fieldSet}>
      <legend className={classNames(styles.legend, size === sizes.SMALL && styles.small)}>{legend}</legend>
      {children}
    </fieldset>
  );
}
