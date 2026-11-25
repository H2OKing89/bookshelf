import classNames from 'classnames';
import React, { Component } from 'react';
import formControlProps, { type FormControlProps } from './formControlProps';
import type { SelectionChangeHandler, ValueChange } from './formTypes';
import styles from './TextArea.css';

export type TextAreaChange = ValueChange<string>;

export interface TextAreaProps extends FormControlProps {
  className?: string;
  name: string;
  value?: string | number | readonly string[] | null;
  readOnly?: boolean;
  autoFocus?: boolean;
  placeholder?: string;
  hasError?: boolean;
  hasWarning?: boolean;
  onChange: (change: TextAreaChange) => void;
  onFocus?: React.FocusEventHandler<HTMLTextAreaElement>;
  onBlur?: React.FocusEventHandler<HTMLTextAreaElement>;
  onSelectionChange?: SelectionChangeHandler;
}

class TextArea extends Component<TextAreaProps> {

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

  private _input: HTMLTextAreaElement | null = null;
  private _selectionStart: number | null = null;
  private _selectionEnd: number | null = null;
  private _selectionTimeout: ReturnType<typeof setTimeout> | undefined = undefined;
  private _isMouseTarget = false;

  //
  // Control

  setInputRef = (ref: HTMLTextAreaElement | null) => {
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

  onChange = (event: React.ChangeEvent<HTMLTextAreaElement>) => {
    const {
      name,
      onChange
    } = this.props;

    const payload: TextAreaChange = {
      name,
      value: event.target.value
    };

    onChange(payload);
  };

  onFocus = (event: React.FocusEvent<HTMLTextAreaElement>) => {
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

  //
  // Render

  render() {
    const {
      className = styles.input,
      readOnly = false,
      autoFocus = false,
      placeholder,
      name,
      value,
      hasError,
      hasWarning,
      onBlur
    } = this.props;

    return (
      <textarea
        {...formControlProps(this.props)}
        ref={this.setInputRef}
        readOnly={readOnly}
        autoFocus={autoFocus}
        placeholder={placeholder}
        className={classNames(
          className,
          readOnly && styles.readOnly,
          hasError && styles.hasError,
          hasWarning && styles.hasWarning
        )}
        name={name}
        value={value ?? ''}
        onChange={this.onChange}
        onFocus={this.onFocus}
        onBlur={onBlur}
        onKeyUp={this.onKeyUp}
        onMouseDown={this.onMouseDown}
        onMouseUp={this.onMouseUp}
      />
    );
  }
}

export default TextArea;
