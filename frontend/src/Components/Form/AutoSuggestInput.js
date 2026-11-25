import classNames from 'classnames';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Autosuggest from 'react-autosuggest';
import Portal from 'Components/Portal';
import AnchoredPopup, { popupMaxHeight } from 'Components/Positioning/AnchoredPopup';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import styles from './AutoSuggestInput.css';

class AutoSuggestInput extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = { reference: null };
    this._id = getUniqueElementId();
  }

  setReference = (reference) => {
    if (reference !== this.state.reference) {
      this.setState({ reference });
    }
  };

  //
  // Control

  renderInputComponent = (inputProps) => {
    const { renderInputComponent } = this.props;

    if (renderInputComponent) {
      return renderInputComponent(inputProps, this.setReference);
    }

    return (
      <div ref={this.setReference}>
        <input {...inputProps} />
      </div>
    );
  };

  renderSuggestionsContainer = ({ containerProps, children }) => {
    const { minHeight, maxHeight, enforceMaxHeight } = this.props;

    return (
      <Portal>
        <AnchoredPopup
          reference={this.state.reference}
          open={Boolean(children)}
          placement="bottom-start"
          flipPadding={minHeight}
          fit={true}
          matchWidth="exact"
          maxHeight={enforceMaxHeight ? maxHeight : undefined}
        >
          {({ ref, style }) => (
            <div
              ref={ref}
              style={style}
              className={children ? styles.suggestionsContainerOpen : undefined}
            >
              <div
                {...containerProps}
                style={{ maxHeight: popupMaxHeight }}
              >
                {children}
              </div>
            </div>
          )}
        </AnchoredPopup>
      </Portal>
    );
  };

  //
  // Listeners

  onInputChange = (event, { newValue }) => {
    this.props.onChange({
      name: this.props.name,
      value: newValue
    });
  };

  onInputKeyDown = (event) => {
    const {
      name,
      value,
      suggestions,
      onChange
    } = this.props;

    if (
      event.key === 'Tab' &&
      suggestions.length &&
      suggestions[0] !== this.props.value
    ) {
      event.preventDefault();

      if (value) {
        onChange({
          name,
          value: suggestions[0]
        });
      }
    }
  };

  //
  // Render

  render() {
    const {
      forwardedRef,
      className,
      inputContainerClassName,
      id,
      name,
      'aria-label': ariaLabel,
      'aria-labelledby': labelledBy,
      'aria-describedby': describedBy,
      'aria-invalid': invalid,
      value,
      placeholder,
      suggestions,
      hasError,
      hasWarning,
      getSuggestionValue,
      renderSuggestion,
      onInputChange,
      onInputKeyDown,
      onInputFocus,
      onInputBlur,
      onSuggestionsFetchRequested,
      onSuggestionsClearRequested,
      onSuggestionSelected,
      ...otherProps
    } = this.props;
    const inputId = id ?? this._id;

    const inputProps = {
      id: inputId,
      'aria-label': ariaLabel,
      'aria-labelledby': labelledBy,
      'aria-describedby': describedBy,
      'aria-invalid': invalid,
      className: classNames(
        className,
        hasError && styles.hasError,
        hasWarning && styles.hasWarning
      ),
      name,
      value,
      placeholder,
      autoComplete: 'off',
      spellCheck: false,
      onChange: onInputChange || this.onInputChange,
      onKeyDown: onInputKeyDown || this.onInputKeyDown,
      onFocus: onInputFocus,
      onBlur: onInputBlur
    };

    const theme = {
      container: inputContainerClassName,
      containerOpen: styles.suggestionsContainerOpen,
      suggestionsContainer: styles.suggestionsContainer,
      suggestionsList: styles.suggestionsList,
      suggestion: styles.suggestion,
      suggestionHighlighted: styles.suggestionHighlighted
    };

    return (
      <Autosuggest
        {...otherProps}
        ref={forwardedRef}
        id={inputId}
        inputProps={inputProps}
        theme={theme}
        suggestions={suggestions}
        getSuggestionValue={getSuggestionValue}
        renderInputComponent={this.renderInputComponent}
        renderSuggestionsContainer={this.renderSuggestionsContainer}
        renderSuggestion={renderSuggestion}
        onSuggestionSelected={onSuggestionSelected}
        onSuggestionsFetchRequested={onSuggestionsFetchRequested}
        onSuggestionsClearRequested={onSuggestionsClearRequested}
      />
    );
  }
}

AutoSuggestInput.propTypes = {
  forwardedRef: PropTypes.func,
  className: PropTypes.string.isRequired,
  inputContainerClassName: PropTypes.string.isRequired,
  id: PropTypes.string,
  name: PropTypes.string.isRequired,
  'aria-label': PropTypes.string,
  'aria-labelledby': PropTypes.string,
  'aria-describedby': PropTypes.string,
  'aria-invalid': PropTypes.oneOfType([PropTypes.bool, PropTypes.string]),
  value: PropTypes.oneOfType([PropTypes.string, PropTypes.array]),
  placeholder: PropTypes.string,
  suggestions: PropTypes.array.isRequired,
  hasError: PropTypes.bool,
  hasWarning: PropTypes.bool,
  enforceMaxHeight: PropTypes.bool.isRequired,
  minHeight: PropTypes.number.isRequired,
  maxHeight: PropTypes.number.isRequired,
  getSuggestionValue: PropTypes.func.isRequired,
  renderInputComponent: PropTypes.elementType,
  renderSuggestion: PropTypes.func.isRequired,
  onInputChange: PropTypes.func,
  onInputKeyDown: PropTypes.func,
  onInputFocus: PropTypes.func,
  onInputBlur: PropTypes.func.isRequired,
  onSuggestionsFetchRequested: PropTypes.func.isRequired,
  onSuggestionsClearRequested: PropTypes.func.isRequired,
  onSuggestionSelected: PropTypes.func,
  onChange: PropTypes.func.isRequired
};

AutoSuggestInput.defaultProps = {
  className: styles.input,
  inputContainerClassName: styles.inputContainer,
  enforceMaxHeight: true,
  minHeight: 50,
  maxHeight: 200
};

export default AutoSuggestInput;
