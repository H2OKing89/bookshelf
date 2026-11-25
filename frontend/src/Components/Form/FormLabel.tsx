import classNames from 'classnames';
import React, { useContext } from 'react';
import { sizes } from 'Helpers/Props';
import type { Size } from 'Helpers/Props/sizes';
import FormGroupContext from './FormGroupContext';
import styles from './FormLabel.css';

export interface FormLabelProps extends React.LabelHTMLAttributes<HTMLLabelElement> {
  component?: 'label' | 'span';
  errorClassName?: string;
  size?: Size;
  name?: string;
  hasError?: boolean;
  isAdvanced?: boolean;
}

const sizeClasses: Partial<Record<Size, string>> = styles;

function FormLabel(props: FormLabelProps) {
  const {
    children,
    className = styles.label,
    component,
    errorClassName = styles.hasError,
    size = sizes.LARGE,
    name,
    id,
    htmlFor,
    hasError,
    isAdvanced = false,
    ...otherProps
  } = props;
  const association = useContext(FormGroupContext);
  const Component = component ?? (association?.isGroup ? 'span' : 'label');

  return (
    <Component
      {...otherProps}
      className={classNames(
        className,
        sizeClasses[size],
        hasError && errorClassName,
        isAdvanced && styles.isAdvanced
      )}
      id={id ?? association?.labelId}
      htmlFor={Component === 'label' ? htmlFor ?? name ?? association?.inputId : undefined}
    >
      {children}
    </Component>
  );
}

export default FormLabel;
