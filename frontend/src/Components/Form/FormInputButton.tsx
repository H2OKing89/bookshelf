import classNames from 'classnames';
import Button, { type ButtonProps } from 'Components/Link/Button';
import SpinnerButton, { type SpinnerButtonProps } from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import styles from './FormInputButton.css';

export interface FormInputButtonProps extends ButtonProps {
  isLastButton?: boolean;
  canSpin?: boolean;
  isSpinning?: boolean;
  spinnerIcon?: SpinnerButtonProps['spinnerIcon'];
}

function FormInputButton(props: FormInputButtonProps) {
  const {
    className = styles.button,
    canSpin = false,
    isLastButton = true,
    isSpinning = false,
    spinnerIcon,
    ...otherProps
  } = props;

  if (canSpin) {
    return (
      <SpinnerButton
        className={classNames(
          className,
          !isLastButton && styles.middleButton
        )}
        kind={kinds.PRIMARY}
        isSpinning={isSpinning}
        spinnerIcon={spinnerIcon}
        {...otherProps}
      />
    );
  }

  return (
    <Button
      className={classNames(
        className,
        !isLastButton && styles.middleButton
      )}
      kind={kinds.PRIMARY}
      {...otherProps}
    />
  );
}

export default FormInputButton;
