import classNames from 'classnames';
import PropTypes from 'prop-types';
import React from 'react';
import Icon from 'Components/Icon';
import Link from './Link';
import styles from './IconButton.css';

function IconButton(props) {
  const {
    className,
    iconClassName,
    name,
    kind,
    size,
    isSpinning,
    isDisabled,
    title,
    'aria-label': ariaLabel,
    ...otherProps
  } = props;

  return (
    <Link
      className={classNames(
        className,
        isDisabled && styles.isDisabled
      )}
      aria-label={ariaLabel || title}
      title={title}
      isDisabled={isDisabled}
      {...otherProps}
    >
      <Icon
        aria-hidden={true}
        className={iconClassName}
        name={name}
        kind={kind}
        size={size}
        isSpinning={isSpinning}
      />
    </Link>
  );
}

IconButton.propTypes = {
  ...Link.propTypes,
  className: PropTypes.string.isRequired,
  iconClassName: PropTypes.string,
  kind: PropTypes.string,
  name: PropTypes.object.isRequired,
  size: PropTypes.number,
  title: PropTypes.string,
  'aria-label': PropTypes.string,
  isSpinning: PropTypes.bool,
  isDisabled: PropTypes.bool
};

IconButton.defaultProps = {
  className: styles.button,
  size: 12
};

export default IconButton;
