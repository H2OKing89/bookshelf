import React from 'react';
import { icons } from 'Helpers/Props';
import Icon, { type IconProps } from './Icon';

export interface SpinnerIconProps extends IconProps {
  spinningName?: IconProps['name'];
  isSpinning: boolean;
}

function SpinnerIcon(props: SpinnerIconProps) {
  const {
    name,
    spinningName = icons.SPINNER,
    isSpinning,
    ...otherProps
  } = props;

  return (
    <Icon
      name={isSpinning ? (spinningName || name) : name}
      isSpinning={isSpinning}
      {...otherProps}
    />
  );
}


export default SpinnerIcon;
