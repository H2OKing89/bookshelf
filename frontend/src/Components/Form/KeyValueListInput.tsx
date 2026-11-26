import classNames from 'classnames';
import { Component } from 'react';
import formControlProps, { type FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';
import KeyValueListInputItem from './KeyValueListInputItem';
import styles from './KeyValueListInput.css';

export interface KeyValueItem {
  key: string;
  value: string | number;
}

export interface KeyValueListInputProps extends FormControlProps {
  className?: string;
  name: string;
  value?: KeyValueItem[];
  keyPlaceholder?: string;
  valuePlaceholder?: string;
  hasError?: boolean;
  hasWarning?: boolean;
  onChange: (change: ValueChange<KeyValueItem[]>) => void;
}

interface KeyValueListInputState {
  isFocused: boolean;
}

class KeyValueListInput extends Component<KeyValueListInputProps, KeyValueListInputState> {

  //
  // Lifecycle

  constructor(props: KeyValueListInputProps) {
    super(props);

    this.state = {
      isFocused: false
    };
  }

  //
  // Listeners

  onItemChange = (index: number | undefined, itemValue: KeyValueItem) => {
    const {
      name,
      value = [],
      onChange
    } = this.props;

    const newValue = [...value];

    if (index == null) {
      newValue.push(itemValue);
    } else {
      newValue.splice(index, 1, itemValue);
    }

    onChange({
      name,
      value: newValue
    });
  };

  onRemoveItem = (index: number) => {
    const {
      name,
      value = [],
      onChange
    } = this.props;

    const newValue = [...value];
    newValue.splice(index, 1);

    onChange({
      name,
      value: newValue
    });
  };

  onFocus = () => {
    this.setState({
      isFocused: true
    });
  };

  onBlur = () => {
    this.setState({
      isFocused: false
    });

    const {
      name,
      value = [],
      onChange
    } = this.props;

    const newValue = value.filter((item) => item.key !== '' || item.value !== '');

    if (newValue.length !== value.length) {
      onChange({
        name,
        value: newValue
      });
    }
  };

  //
  // Render

  render() {
    const {
      className = styles.inputContainer,
      value = [],
      keyPlaceholder,
      valuePlaceholder,
      hasError,
      hasWarning
    } = this.props;

    const { isFocused } = this.state;
    const {
      id,
      ...attributes
    } = formControlProps(this.props);

    return (
      <div
        {...attributes}
        role="group"
        className={classNames(
          className,
          isFocused && styles.isFocused,
          hasError && styles.hasError,
          hasWarning && styles.hasWarning
        )}
      >
        {
          [...value, { key: '', value: '' }].map((v, index) => {
            return (
              <KeyValueListInputItem
                key={index}
                id={index === 0 ? id : undefined}
                index={index}
                keyValue={v.key}
                value={v.value}
                keyPlaceholder={keyPlaceholder}
                valuePlaceholder={valuePlaceholder}
                isNew={index === value.length}
                onChange={this.onItemChange}
                onRemove={this.onRemoveItem}
                onFocus={this.onFocus}
                onBlur={this.onBlur}
              />
            );
          })
        }
      </div>
    );
  }
}

export default KeyValueListInput;
