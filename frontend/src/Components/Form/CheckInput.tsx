import classNames from 'classnames';
import React, { Component } from 'react';
import Icon from 'Components/Icon';
import { icons, kinds } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import formControlProps, { type FormControlProps } from './formControlProps';
import FormInputHelpText from './FormInputHelpText';
import type { ValueChange } from './formTypes';
import styles from './CheckInput.css';

export interface CheckInputChange extends ValueChange<boolean> {
  shiftKey?: boolean;
}

export interface CheckInputProps extends FormControlProps {
  className?: string;
  containerClassName?: string;
  name: string;
  value?: boolean | string | null;
  checkedValue?: boolean;
  uncheckedValue?: boolean;
  helpText?: string;
  helpTextId?: string;
  helpTextWarning?: string;
  helpTextWarningId?: string;
  isDisabled?: boolean;
  kind?: Kind;
  onChange: (change: CheckInputChange) => void;
}

const checkedClasses: Partial<Record<Kind, string>> = {
  danger: styles.dangerIsChecked,
  primary: styles.primaryIsChecked,
  success: styles.successIsChecked,
  warning: styles.warningIsChecked
};

function getShiftKey(event: Event): boolean | undefined {
  return 'shiftKey' in event && typeof event.shiftKey === 'boolean' ? event.shiftKey : undefined;
}

class CheckInput extends Component<CheckInputProps> {

  //
  // Lifecycle

  componentDidMount() {
    this.setIndeterminate();
  }

  componentDidUpdate() {
    this.setIndeterminate();
  }

  private _checkbox: HTMLInputElement | null = null;

  //
  // Control

  setIndeterminate() {
    if (!this._checkbox) {
      return;
    }

    const {
      value,
      uncheckedValue = false,
      checkedValue = true
    } = this.props;

    this._checkbox.indeterminate = value !== uncheckedValue && value !== checkedValue;
  }

  toggleChecked = (checked: boolean, shiftKey?: boolean) => {
    const {
      name,
      value,
      checkedValue = true,
      uncheckedValue = false
    } = this.props;

    const newValue = checked ? checkedValue : uncheckedValue;

    if (value !== newValue) {
      this.props.onChange({
        name,
        value: newValue,
        shiftKey
      });
    }
  };

  //
  // Listeners

  setRef = (ref: HTMLInputElement | null) => {
    this._checkbox = ref;
  };

  onClick = (event: React.MouseEvent<HTMLLabelElement>) => {
    if (this.props.isDisabled || !this._checkbox || event.target === this._checkbox) {
      return;
    }

    const shiftKey = getShiftKey(event.nativeEvent);
    const checked = !this._checkbox.checked;

    event.preventDefault();
    this.toggleChecked(checked, shiftKey);
  };

  onChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const checked = event.target.checked;
    const shiftKey = getShiftKey(event.nativeEvent);

    this.toggleChecked(checked, shiftKey);
  };

  //
  // Render

  render() {
    const {
      className = styles.input,
      containerClassName = styles.container,
      name,
      value,
      checkedValue = true,
      uncheckedValue = false,
      helpText,
      helpTextId,
      helpTextWarning,
      helpTextWarningId,
      isDisabled,
      kind = kinds.PRIMARY
    } = this.props;

    const isChecked = value === checkedValue;
    const isUnchecked = value === uncheckedValue;
    const isIndeterminate = !isChecked && !isUnchecked;

    return (
      <div className={containerClassName}>
        <label
          className={styles.label}
          onClick={this.onClick}
        >
          <input
            {...formControlProps(this.props)}
            ref={this.setRef}
            className={styles.checkbox}
            type="checkbox"
            name={name}
            checked={isChecked}
            disabled={isDisabled}
            onChange={this.onChange}
          />

          <div
            className={classNames(
              className,
              isChecked ? checkedClasses[kind] : styles.isNotChecked,
              isIndeterminate && styles.isIndeterminate,
              isDisabled && styles.isDisabled
            )}
          >
            {
              isChecked &&
                <Icon name={icons.CHECK} />
            }

            {
              isIndeterminate &&
                <Icon name={icons.CHECK_INDETERMINATE} />
            }
          </div>

          {
            helpText &&
              <FormInputHelpText
                className={styles.helpText}
                id={helpTextId}
                text={helpText}
              />
          }

          {
            !helpText && helpTextWarning &&
              <FormInputHelpText
                className={styles.helpText}
                id={helpTextWarningId}
                text={helpTextWarning}
                isWarning={true}
              />
          }
        </label>
      </div>
    );
  }
}

export default CheckInput;
