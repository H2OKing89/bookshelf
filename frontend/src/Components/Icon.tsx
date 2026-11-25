import type { IconProp } from '@fortawesome/fontawesome-svg-core';
import { FontAwesomeIcon, type FontAwesomeIconProps } from '@fortawesome/react-fontawesome';
import classNames from 'classnames';
import React, { PureComponent } from 'react';
import { kinds } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import styles from './Icon.css';

const kindClasses: Partial<Record<Kind, string>> = styles;

export interface IconProps extends Omit<FontAwesomeIconProps, 'icon' | 'size' | 'title' | 'spin' | 'name' | 'ref'> {
  containerClassName?: string;
  name: IconProp;
  kind?: Kind;
  size?: number;
  title?: string | (() => string);
  isSpinning?: boolean;
}

class Icon extends PureComponent<IconProps> {

  //
  // Render

  render() {
    const {
      containerClassName,
      className,
      name,
      kind = kinds.DEFAULT,
      size = 14,
      title,
      isSpinning = false,
      fixedWidth = false,
      ...otherProps
    } = this.props;

    const icon = (
      <FontAwesomeIcon
        className={classNames(
          className,
          kindClasses[kind]
        )}
        icon={name}
        spin={isSpinning}
        fixedWidth={fixedWidth}
        style={{
          fontSize: `${size}px`
        }}
        {...otherProps}
      />
    );

    if (title) {
      return (
        <span
          className={containerClassName}
          title={typeof title === 'function' ? title() : title}
        >
          {icon}
        </span>
      );
    }

    return icon;
  }
}


export default Icon;
