import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import { kinds } from 'Helpers/Props';
import formControlProps, { type FormControlProps } from './formControlProps';

interface OAuthInputProps extends FormControlProps {
  label?: string;
  authorizing: boolean;
  error?: unknown;
  onPress: () => void;
}

function OAuthInput(props: OAuthInputProps) {
  const {
    label = 'Start OAuth',
    authorizing,
    error,
    onPress
  } = props;

  return (
    <div>
      <SpinnerErrorButton
        {...formControlProps(props)}
        aria-labelledby={undefined}
        aria-label={label}
        kind={kinds.PRIMARY}
        isSpinning={authorizing}
        error={error}
        onPress={onPress}
      >
        {label}
      </SpinnerErrorButton>
    </div>
  );
}

export default OAuthInput;
