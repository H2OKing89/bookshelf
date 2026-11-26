import jdu from 'jdu';
import { Component } from 'react';
import AutoSuggestInput from './AutoSuggestInput';
import type { FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';

export interface AutoCompleteInputProps extends FormControlProps {
  name: string;
  value?: string | null;
  values: readonly string[];
  className?: string;
  hasError?: boolean;
  hasWarning?: boolean;
  isDisabled?: boolean;
  placeholder?: string;
  onChange: (change: ValueChange<string>) => void;
}

export function filterAutoComplete(values: readonly string[], query: string): string[] {
  const normalized = jdu.replace(query).toLowerCase();
  return values.filter((value) => jdu.replace(value).toLowerCase().includes(normalized));
}

class AutoCompleteInput extends Component<AutoCompleteInputProps, { suggestions: string[] }> {
  state: { suggestions: string[] } = { suggestions: [] };

  onInputBlur = () => {
    this.setState({ suggestions: [] });
  };

  onSuggestionsFetchRequested = ({ value }: { value: string }) => {
    this.setState({ suggestions: filterAutoComplete(this.props.values, value) });
  };

  onSuggestionsClearRequested = () => {
    this.setState({ suggestions: [] });
  };

  render() {
    const { value, ...otherProps } = this.props;

    return (
      <AutoSuggestInput
        {...otherProps}
        value={value ?? ''}
        suggestions={this.state.suggestions}
        getSuggestionValue={(item) => item}
        renderSuggestion={(item) => item}
        onInputBlur={this.onInputBlur}
        onSuggestionsFetchRequested={this.onSuggestionsFetchRequested}
        onSuggestionsClearRequested={this.onSuggestionsClearRequested}
      />
    );
  }
}

export default AutoCompleteInput;
