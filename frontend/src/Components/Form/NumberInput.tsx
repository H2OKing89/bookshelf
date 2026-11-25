import React, { Component } from 'react';
import type { ValueChange } from './formTypes';
import TextInput, { type TextInputChange, type TextInputProps } from './TextInput';

export interface NumberInputProps extends Omit<TextInputProps, 'type' | 'value' | 'onChange' | 'min' | 'max'> {
  value?: number | null;
  min?: number;
  max?: number;
  isFloat?: boolean;
  onChange: (change: ValueChange<number | null>) => void;
}

interface NumberInputState {
  value: string;
  isFocused: boolean;
}

export function parseValue(props: Pick<NumberInputProps, 'min' | 'max' | 'isFloat'>, value: string | null | undefined): number | null {
  const {
    isFloat,
    min,
    max
  } = props;

  if (value == null || value === '') {
    return null;
  }

  let newValue = isFloat ? parseFloat(value) : parseInt(value);

  if (!Number.isFinite(newValue)) {
    return null;
  }

  if (min != null && newValue < min) {
    newValue = min;
  } else if (max != null && newValue > max) {
    newValue = max;
  }

  return newValue;
}

class NumberInput extends Component<NumberInputProps, NumberInputState> {

  //
  // Lifecycle

  constructor(props: NumberInputProps) {
    super(props);

    this.state = {
      value: props.value == null ? '' : props.value.toString(),
      isFocused: false
    };
  }

  componentDidUpdate(prevProps: NumberInputProps) {
    const { value } = this.props;

    if ((value == null || Number.isFinite(value)) && value !== prevProps.value && !this.state.isFocused) {
      this.setState({
        value: value == null ? '' : value.toString()
      });
    }
  }

  //
  // Listeners

  onChange = ({ name, value }: TextInputChange) => {
    this.setState({ value });

    this.props.onChange({
      name,
      value: parseValue(this.props, value)
    });

  };

  onFocus = () => {
    this.setState({ isFocused: true });
  };

  onBlur = () => {
    const {
      name,
      onChange
    } = this.props;

    const { value } = this.state;
    const parsedValue = parseValue(this.props, value);
    const stringValue = parsedValue == null ? '' : parsedValue.toString();

    if (stringValue === value) {
      this.setState({ isFocused: false });
    } else {
      this.setState({
        value: stringValue,
        isFocused: false
      });
    }

    onChange({
      name,
      value: parsedValue
    });
  };

  //
  // Render

  render() {
    const value = this.state.value;

    return (
      <TextInput
        {...this.props}
        type="number"
        value={value == null ? '' : value}
        onChange={this.onChange}
        onBlur={this.onBlur}
        onFocus={this.onFocus}
      />
    );
  }
}

export default NumberInput;
