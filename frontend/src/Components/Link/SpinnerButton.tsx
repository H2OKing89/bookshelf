import classNames from 'classnames';
import Icon, { type IconProps } from 'Components/Icon';
import { icons } from 'Helpers/Props';
import Button, { type ButtonProps } from './Button';
import styles from './SpinnerButton.css';

export interface SpinnerButtonProps extends ButtonProps {
  isSpinning: boolean;
  spinnerIcon?: IconProps['name'];
}

function SpinnerButton(props: SpinnerButtonProps) {
  const {
    className = styles.button,
    isSpinning,
    isDisabled,
    spinnerIcon = icons.SPINNER,
    children,
    ...otherProps
  } = props;

  return (
    <Button
      className={classNames(
        className,
        styles.button,
        isSpinning && styles.isSpinning
      )}
      isDisabled={isDisabled || isSpinning}
      {...otherProps}
    >
      <span className={styles.spinnerContainer}>
        <Icon
          aria-hidden={true}
          className={styles.spinner}
          name={spinnerIcon}
          isSpinning={true}
        />
      </span>

      <span className={styles.label}>
        {children}
      </span>
    </Button>
  );
}


export default SpinnerButton;
