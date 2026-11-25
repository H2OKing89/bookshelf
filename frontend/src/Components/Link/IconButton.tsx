import classNames from 'classnames';
import React from 'react';
import Icon, { type IconProps } from 'Components/Icon';
import Link, { type LinkProps } from './Link';
import styles from './IconButton.css';

export interface IconButtonProps extends Omit<LinkProps, 'size' | 'name' | 'kind'> {
  iconClassName?: string;
  name: IconProps['name'];
  kind?: IconProps['kind'];
  size?: number;
  isSpinning?: boolean;
}

function IconButton(props: IconButtonProps) {
  const {
    className = styles.button,
    iconClassName,
    name,
    kind,
    size = 12,
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


export default IconButton;
