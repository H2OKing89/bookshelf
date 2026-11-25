import classNames from 'classnames';
import React, { Component } from 'react';
import formControlProps, { type FormControlProps } from './formControlProps';
import type { SelectOption, ValueChange } from './formTypes';
export type { SelectOption } from './formTypes';
import styles from './SelectInput.css';

export interface SelectInputProps extends FormControlProps {
  className?: string;
  disabledClassName?: string;
  name: string;
  value?: string | number | null;
  values: readonly SelectOption[];
  isDisabled?: boolean;
  hasError?: boolean;
  hasWarning?: boolean;
  autoFocus?: boolean;
  onChange: (change: ValueChange<string>) => void;
  onBlur?: React.FocusEventHandler<HTMLSelectElement>;
}

class SelectInput extends Component<SelectInputProps> {

  //
  // Listeners

  onChange = (event: React.ChangeEvent<HTMLSelectElement>) => {
    this.props.onChange({
      name: this.props.name,
      value: event.target.value
    });
  };

  //
  // Render

  render() {
    const {
      className = styles.select,
      disabledClassName = styles.isDisabled,
      name,
      value,
      values,
      isDisabled = false,
      hasError,
      hasWarning,
      autoFocus = false,
      onBlur
    } = this.props;

    return (
      <select
        {...formControlProps(this.props)}
        className={classNames(
          className,
          hasError && styles.hasError,
          hasWarning && styles.hasWarning,
          isDisabled && disabledClassName
        )}
        disabled={isDisabled}
        name={name}
        value={value ?? ''}
        autoFocus={autoFocus}
        onChange={this.onChange}
        onBlur={onBlur}
      >
        {
          values.map((option) => {
            const {
              key,
              value: optionValue,
              isDisabled: optionIsDisabled = false,
              ...otherOptionProps
            } = option;

            return (
              <option
                key={key}
                value={key}
                disabled={optionIsDisabled}
                {...otherOptionProps}
              >
                {typeof optionValue === 'function' ? optionValue() : optionValue}
              </option>
            );
          })
        }
      </select>
    );
  }
}

export default SelectInput;
