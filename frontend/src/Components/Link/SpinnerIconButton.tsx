import { icons } from 'Helpers/Props';
import IconButton, { type IconButtonProps } from './IconButton';

export interface SpinnerIconButtonProps extends IconButtonProps {
  spinningName?: IconButtonProps['name'];
}

function SpinnerIconButton(props: SpinnerIconButtonProps) {
  const {
    name,
    spinningName = icons.SPINNER,
    isDisabled = false,
    isSpinning = false,
    ...otherProps
  } = props;

  return (
    <IconButton
      name={isSpinning ? (spinningName || name) : name}
      isDisabled={isDisabled || isSpinning}
      isSpinning={isSpinning}
      {...otherProps}
    />
  );
}

export default SpinnerIconButton;
