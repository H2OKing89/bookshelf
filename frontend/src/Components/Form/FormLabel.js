import classNames from 'classnames';
import PropTypes from 'prop-types';
import React, { useContext } from 'react';
import { sizes } from 'Helpers/Props';
import FormGroupContext from './FormGroupContext';
import styles from './FormLabel.css';

function FormLabel(props) {
  const {
    children,
    className,
    component,
    errorClassName,
    size,
    name,
    id,
    htmlFor,
    hasError,
    isAdvanced,
    ...otherProps
  } = props;
  const association = useContext(FormGroupContext);
  const Component = component ?? (association?.isGroup ? 'span' : 'label');

  return (
    <Component
      {...otherProps}
      className={classNames(
        className,
        styles[size],
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

FormLabel.propTypes = {
  children: PropTypes.oneOfType([PropTypes.node, PropTypes.string]).isRequired,
  className: PropTypes.string,
  component: PropTypes.oneOf(['label', 'span']),
  errorClassName: PropTypes.string,
  size: PropTypes.oneOf(sizes.all),
  name: PropTypes.string,
  id: PropTypes.string,
  htmlFor: PropTypes.string,
  hasError: PropTypes.bool,
  isAdvanced: PropTypes.bool
};

FormLabel.defaultProps = {
  className: styles.label,
  errorClassName: styles.hasError,
  isAdvanced: false,
  size: sizes.LARGE
};

export default FormLabel;
