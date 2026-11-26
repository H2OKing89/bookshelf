import classNames from 'classnames';
import ReCAPTCHA from 'react-google-recaptcha';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import formControlProps, { type FormControlProps } from './formControlProps';
import FormInputButton from './FormInputButton';
import TextInput, { type TextInputChange } from './TextInput';
import styles from './CaptchaInput.css';

interface CaptchaInputProps extends FormControlProps {
  className?: string;
  name: string;
  value?: string | null;
  hasError?: boolean;
  hasWarning?: boolean;
  refreshing: boolean;
  siteKey?: string | null;
  secretToken?: string | null;
  onChange: (change: TextInputChange) => void;
  onRefreshPress: () => void;
  onCaptchaChange: (response: string | null) => void;
}

function CaptchaInput(props: CaptchaInputProps) {
  const {
    className = styles.input,
    name,
    value = '',
    hasError,
    hasWarning,
    refreshing,
    siteKey,
    secretToken,
    onChange,
    onRefreshPress,
    onCaptchaChange
  } = props;

  return (
    <div>
      <div className={styles.captchaInputWrapper}>
        <TextInput
          {...formControlProps(props)}
          className={classNames(
            className,
            styles.hasButton,
            hasError && styles.hasError,
            hasWarning && styles.hasWarning
          )}
          name={name}
          value={value}
          onChange={onChange}
        />

        <FormInputButton
          onPress={onRefreshPress}
        >
          <Icon
            name={icons.REFRESH}
            isSpinning={refreshing}
          />
        </FormInputButton>
      </div>

      {
        !!siteKey && !!secretToken &&
          <div className={styles.recaptchaWrapper}>
            <ReCAPTCHA
              sitekey={siteKey}
              stoken={secretToken}
              onChange={onCaptchaChange}
            />
          </div>
      }
    </div>
  );
}

export default CaptchaInput;
