// The locked react-autosuggest library has no declarations. Model the single
// section API and instance methods that our wrapper actually uses.
declare module 'react-autosuggest' {
  import type { Component, ComponentPropsWithRef, FocusEventHandler, InputHTMLAttributes, KeyboardEventHandler, ReactNode, SyntheticEvent } from 'react';

  export interface ChangeEvent {
    newValue: string;
    method: string;
  }

  export interface SuggestionSelectedEvent<Suggestion> {
    suggestion: Suggestion;
    suggestionValue: string;
  }

  export interface SuggestionFetchEvent {
    value: string;
    reason?: string;
  }

  export interface SuggestionRenderOptions {
    query: string;
    isHighlighted?: boolean;
  }

  export type RenderInputProps = ComponentPropsWithRef<'input'>;

  export interface InputProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'onKeyDown' | 'onFocus' | 'onBlur'> {
    value: string;
    onChange: (event: SyntheticEvent, change: ChangeEvent) => void;
    onKeyDown?: KeyboardEventHandler<HTMLInputElement>;
    onFocus?: FocusEventHandler<HTMLInputElement>;
    onBlur?: FocusEventHandler<HTMLInputElement>;
  }

  interface Props<Suggestion> {
    id?: string;
    inputProps: InputProps;
    theme?: Record<string, string | undefined>;
    suggestions: readonly Suggestion[];
    getSuggestionValue: (suggestion: Suggestion) => string;
    renderSuggestion: (suggestion: Suggestion, options: SuggestionRenderOptions) => ReactNode;
    renderInputComponent?: (props: RenderInputProps) => ReactNode;
    renderSuggestionsContainer?: (props: {
      containerProps: ComponentPropsWithRef<'div'>;
      children: ReactNode;
      query?: string;
    }) => ReactNode;
    shouldRenderSuggestions?: (value: string) => boolean;
    onSuggestionSelected?: (event: SyntheticEvent, selection: SuggestionSelectedEvent<Suggestion>) => void;
    onSuggestionsFetchRequested: (request: SuggestionFetchEvent) => void;
    onSuggestionsClearRequested: () => void;
    focusInputOnSuggestionClick?: boolean;
    alwaysRenderSuggestions?: boolean;
    highlightFirstSuggestion?: boolean;
  }

  export default class Autosuggest<Suggestion> extends Component<Props<Suggestion>> {
    input: HTMLInputElement | null;
    getHighlightedSuggestion(): Suggestion | null;
  }
}
