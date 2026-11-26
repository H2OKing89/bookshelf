import classNames from 'classnames';
import { Component, type ComponentPropsWithRef, type FocusEventHandler, type KeyboardEvent, type KeyboardEventHandler, type ReactNode, type SyntheticEvent } from 'react';
import Autosuggest, { type ChangeEvent, type InputProps, type RenderInputProps, type SuggestionFetchEvent, type SuggestionRenderOptions, type SuggestionSelectedEvent } from 'react-autosuggest';
import Portal from 'Components/Portal';
import AnchoredPopup, { popupMaxHeight } from 'Components/Positioning/AnchoredPopup';
import getUniqueElementId from 'Utilities/getUniqueElementId';
import type { FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';
import styles from './AutoSuggestInput.css';

export interface AutoSuggestInputProps<Suggestion> extends FormControlProps {
  forwardedRef?: (reference: Autosuggest<Suggestion> | null) => void;
  className?: string;
  inputContainerClassName?: string;
  name: string;
  value: string;
  placeholder?: string;
  suggestions: readonly Suggestion[];
  hasError?: boolean;
  hasWarning?: boolean;
  isDisabled?: boolean;
  enforceMaxHeight?: boolean;
  minHeight?: number;
  maxHeight?: number;
  getSuggestionValue: (suggestion: Suggestion) => string;
  renderInputComponent?: (props: RenderInputProps, ref: (element: HTMLElement | null) => void) => ReactNode;
  renderSuggestion: (suggestion: Suggestion, options: SuggestionRenderOptions) => ReactNode;
  shouldRenderSuggestions?: (value: string) => boolean;
  onInputChange?: (event: SyntheticEvent, change: ChangeEvent) => void;
  onInputKeyDown?: KeyboardEventHandler<HTMLInputElement>;
  onInputFocus?: FocusEventHandler<HTMLInputElement>;
  onInputBlur: FocusEventHandler<HTMLInputElement>;
  onSuggestionsFetchRequested: (request: SuggestionFetchEvent) => void;
  onSuggestionsClearRequested: () => void;
  onSuggestionSelected?: (event: SyntheticEvent, selection: SuggestionSelectedEvent<Suggestion>) => void;
  onChange: (change: ValueChange<string>) => void;
  focusInputOnSuggestionClick?: boolean;
  alwaysRenderSuggestions?: boolean;
  highlightFirstSuggestion?: boolean;
}

class AutoSuggestInput<Suggestion> extends Component<AutoSuggestInputProps<Suggestion>, { reference: HTMLElement | null }> {
  state: { reference: HTMLElement | null } = { reference: null };
  private readonly _id = getUniqueElementId();

  setReference = (reference: HTMLElement | null) => {
    if (reference !== this.state.reference) {
      this.setState({ reference });
    }
  };

  renderInputComponent = (inputProps: RenderInputProps) => {
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

  renderSuggestionsContainer = ({ containerProps, children }: { containerProps: ComponentPropsWithRef<'div'>; children: ReactNode }) => {
    const { minHeight = 50, maxHeight = 200, enforceMaxHeight = true } = this.props;

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

  onInputChange = (_event: SyntheticEvent, { newValue }: ChangeEvent) => {
    this.props.onChange({ name: this.props.name, value: newValue });
  };

  onInputKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    const { name, value, suggestions, getSuggestionValue, onChange } = this.props;
    const first = suggestions[0];

    if (event.key === 'Tab' && !event.shiftKey && value && first !== undefined) {
      const suggestionValue = getSuggestionValue(first);

      if (suggestionValue !== value) {
        event.preventDefault();
        onChange({ name, value: suggestionValue });
      }
    }
  };

  render() {
    const {
      forwardedRef,
      className = styles.input,
      inputContainerClassName = styles.inputContainer,
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
      isDisabled,
      getSuggestionValue,
      renderSuggestion,
      onInputChange,
      onInputKeyDown,
      onInputFocus,
      onInputBlur,
      onSuggestionsFetchRequested,
      onSuggestionsClearRequested,
      onSuggestionSelected,
      shouldRenderSuggestions,
      focusInputOnSuggestionClick,
      alwaysRenderSuggestions,
      highlightFirstSuggestion
    } = this.props;
    const inputId = id ?? this._id;

    const inputProps: InputProps = {
      id: inputId,
      'aria-label': ariaLabel,
      'aria-labelledby': labelledBy,
      'aria-describedby': describedBy,
      'aria-invalid': invalid,
      className: classNames(className, hasError && styles.hasError, hasWarning && styles.hasWarning),
      name,
      value,
      placeholder,
      disabled: isDisabled,
      autoComplete: 'off',
      spellCheck: false,
      onChange: onInputChange ?? this.onInputChange,
      onKeyDown: onInputKeyDown ?? this.onInputKeyDown,
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
        ref={forwardedRef}
        id={inputId}
        inputProps={inputProps}
        theme={theme}
        suggestions={suggestions}
        getSuggestionValue={getSuggestionValue}
        renderInputComponent={this.renderInputComponent}
        renderSuggestionsContainer={this.renderSuggestionsContainer}
        renderSuggestion={renderSuggestion}
        shouldRenderSuggestions={shouldRenderSuggestions}
        focusInputOnSuggestionClick={focusInputOnSuggestionClick}
        alwaysRenderSuggestions={alwaysRenderSuggestions}
        highlightFirstSuggestion={highlightFirstSuggestion}
        onSuggestionSelected={onSuggestionSelected}
        onSuggestionsFetchRequested={onSuggestionsFetchRequested}
        onSuggestionsClearRequested={onSuggestionsClearRequested}
      />
    );
  }
}

export default AutoSuggestInput;
