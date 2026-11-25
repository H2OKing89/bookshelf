import classNames from 'classnames';
import React, { Component } from 'react';
import formControlProps, { type FormControlProps } from './formControlProps';
import type { SelectionChangeHandler, ValueChange } from './formTypes';
import styles from './TextInput.css';

export interface TextInputChange extends ValueChange<string> {
  files?: FileList | null;
}

export interface TextInputProps extends FormControlProps {
  className?: string;
  name: string;
  value?: string | number | readonly string[] | null;
  readOnly?: boolean;
  autoFocus?: boolean;
  placeholder?: string;
  hasError?: boolean;
  hasWarning?: boolean;
  onChange: (change: TextInputChange) => void;
  onFocus?: React.FocusEventHandler<HTMLInputElement>;
  onBlur?: React.FocusEventHandler<HTMLInputElement>;
  onSelectionChange?: SelectionChangeHandler;
  type?: React.HTMLInputTypeAttribute;
  hasButton?: boolean;
  step?: number | string;
  min?: number | string;
  max?: number | string;
  onCopy?: React.ClipboardEventHandler<HTMLInputElement>;
}

class TextInput extends Component<TextInputProps> {

  //
  // Lifecycle

  componentDidMount() {
    window.addEventListener('mouseup', this.onDocumentMouseUp);
  }

  componentWillUnmount() {
    window.removeEventListener('mouseup', this.onDocumentMouseUp);

    if (this._selectionTimeout !== undefined) {
      clearTimeout(this._selectionTimeout);
      this._selectionTimeout = undefined;
    }
  }

  private _input: HTMLInputElement | null = null;
  private _selectionStart: number | null = null;
  private _selectionEnd: number | null = null;
  private _selectionTimeout: ReturnType<typeof setTimeout> | undefined = undefined;
  private _isMouseTarget = false;

  //
  // Control

  setInputRef = (ref: HTMLInputElement | null) => {
    this._input = ref;
  };

  selectionChange() {
    if (this._selectionTimeout !== undefined) {
      clearTimeout(this._selectionTimeout);
      this._selectionTimeout = undefined;
    }

    this._selectionTimeout = setTimeout(() => {
      this._selectionTimeout = undefined;

      if (!this._input) {
        return;
      }

      const selectionStart = this._input.selectionStart;
      const selectionEnd = this._input.selectionEnd;

      const selectionChanged = (
        this._selectionStart !== selectionStart ||
        this._selectionEnd !== selectionEnd
      );

      this._selectionStart = selectionStart;
      this._selectionEnd = selectionEnd;

      if (this.props.onSelectionChange && selectionChanged) {
        this.props.onSelectionChange(selectionStart, selectionEnd);
      }
    }, 10);
  }

  //
  // Listeners

  onChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const {
      name,
      type,
      onChange
    } = this.props;

    const payload: TextInputChange = {
      name,
      value: event.target.value
    };

    // Also return the files for a file input type.

    if (type === 'file') {
      payload.files = event.target.files;
    }

    onChange(payload);
  };

  onFocus = (event: React.FocusEvent<HTMLInputElement>) => {
    if (this.props.onFocus) {
      this.props.onFocus(event);
    }

    this.selectionChange();
  };

  onKeyUp = () => {
    this.selectionChange();
  };

  onMouseDown = () => {
    this._isMouseTarget = true;
  };

  onMouseUp = () => {
    this.selectionChange();
  };

  onDocumentMouseUp = () => {
    if (this._isMouseTarget) {
      this.selectionChange();
    }

    this._isMouseTarget = false;
  };

  onWheel = () => {
    if (this.props.type === 'number') {
      this._input?.blur();
    }
  };

  //
  // Render

  render() {
    const {
      className = styles.input,
      type = 'text',
      readOnly = false,
      autoFocus = false,
      placeholder,
      name,
      value,
      hasError,
      hasWarning,
      hasButton,
      step,
      min,
      max,
      onBlur,
      onCopy
    } = this.props;

    return (
      <input
        {...formControlProps(this.props)}
        ref={this.setInputRef}
        type={type}
        readOnly={readOnly}
        autoFocus={autoFocus}
        placeholder={placeholder}
        className={classNames(
          className,
          readOnly && styles.readOnly,
          hasError && styles.hasError,
          hasWarning && styles.hasWarning,
          hasButton && styles.hasButton
        )}
        name={name}
        value={value ?? ''}
        step={step}
        min={min}
        max={max}
        onChange={this.onChange}
        onFocus={this.onFocus}
        onBlur={onBlur}
        onCopy={onCopy}
        onCut={onCopy}
        onKeyUp={this.onKeyUp}
        onMouseDown={this.onMouseDown}
        onMouseUp={this.onMouseUp}
        onWheel={this.onWheel}
      />
    );
  }
}

export default TextInput;
