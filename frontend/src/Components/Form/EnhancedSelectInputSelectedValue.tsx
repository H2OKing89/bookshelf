import classNames from 'classnames';
import React from 'react';
import styles from './EnhancedSelectInputSelectedValue.css';

export interface EnhancedSelectInputSelectedValueProps {
  className?: string;
  id?: string;
  children?: React.ReactNode;
  isDisabled?: boolean;
}

function EnhancedSelectInputSelectedValue(props: EnhancedSelectInputSelectedValueProps) {
  const {
    className = styles.selectedValue,
    id,
    children,
    isDisabled = false
  } = props;

  return (
    <div
      id={id}
      className={classNames(
        className,
        isDisabled && styles.isDisabled
      )}
    >
      {children}
    </div>
  );
}

export default EnhancedSelectInputSelectedValue;
