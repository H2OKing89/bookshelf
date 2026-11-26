import classNames from 'classnames';
import _ from 'lodash';
import { Component, type ComponentType, type KeyboardEvent, type SyntheticEvent } from 'react';
import type Autosuggest from 'react-autosuggest';
import type { ChangeEvent, RenderInputProps, SuggestionSelectedEvent } from 'react-autosuggest';
import { kinds } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import AutoSuggestInput from './AutoSuggestInput';
import type { FormControlProps } from './formControlProps';
import TagInputInput from './TagInputInput';
import TagInputTag, { type TagInputTagProps } from './TagInputTag';
import { type RenderedTag, resolveTag, type TagChoice, type TagEdit, type TagIdentity, type TagSelection } from './tagRenderTypes';
import styles from './TagInput.css';

export interface TagInputProps<Id extends TagIdentity> extends FormControlProps {
  name: string;
  className?: string;
  inputContainerClassName?: string;
  tags: readonly RenderedTag<Id>[];
  tagList: readonly RenderedTag<Id>[];
  allowNew?: boolean;
  kind?: Kind;
  placeholder?: string;
  delimiters?: readonly string[];
  minQueryLength?: number;
  maxSuggestionsLength?: number;
  canEdit?: boolean;
  hasError?: boolean;
  hasWarning?: boolean;
  isDisabled?: boolean;
  tagComponent?: ComponentType<TagInputTagProps<Id>>;
  onTagAdd: (tag: TagChoice<Id>) => void;
  onTagDelete: (tag: TagSelection<Id>) => void;
  onTagReplace?: (tag: TagSelection<Id>, replacement: TagChoice<Id>) => void;
}

interface State<Id extends TagIdentity> {
  value: string;
  suggestions: RenderedTag<Id>[];
  isFocused: boolean;
}

class TagInput<Id extends TagIdentity> extends Component<TagInputProps<Id>, State<Id>> {
  state: State<Id> = { value: '', suggestions: [], isFocused: false };

  componentWillUnmount() {
    this.addTag.cancel();
    clearTimeout(this._refreshTimeout);
  }

  private _autosuggestRef: Autosuggest<RenderedTag<Id>> | null = null;
  private _refreshTimeout: ReturnType<typeof setTimeout> | undefined = undefined;

  setAutosuggestRef = (ref: Autosuggest<RenderedTag<Id>> | null) => {
    this._autosuggestRef = ref;
  };

  getSuggestionValue({ name }: RenderedTag<Id>) {
    return name;
  }

  shouldRenderSuggestions = (value: string) => {
    return !this.props.isDisabled && value.length >= (this.props.minQueryLength ?? 1);
  };

  renderSuggestion({ name }: RenderedTag<Id>) {
    return name;
  }

  addTag = _.debounce((tag: TagChoice<Id>) => {
    if (this.props.isDisabled) {
      return;
    }

    this.props.onTagAdd(tag);
    this.setState({ value: '', suggestions: [] });
  }, 250, { leading: true, trailing: false });

  onTagEdit = ({ value, ...tag }: TagEdit<Id>) => {
    if (this.props.isDisabled) {
      return;
    }

    const currentValue = this.state.value;
    if (currentValue && this.props.onTagReplace) {
      this.props.onTagReplace(tag, { name: currentValue });
    } else {
      this.props.onTagDelete(tag);
    }
    this.setState({ value });
  };

  onTagDelete = (tag: TagSelection<Id>) => {
    if (!this.props.isDisabled) {
      this.props.onTagDelete(tag);
    }
  };

  onInputContainerPress = () => {
    if (!this.props.isDisabled) {
      this._autosuggestRef?.input?.focus();
    }
  };

  onInputChange = (_event: SyntheticEvent, { newValue, method }: ChangeEvent) => {
    if (method === 'type') {
      this.setState({ value: newValue });
    }
  };

  onInputKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    const { tags, allowNew = true, delimiters = ['Tab', 'Enter', ' ', ','], isDisabled } = this.props;
    const { value, suggestions } = this.state;
    const key = event.key;

    if (isDisabled || (key === 'Tab' && event.shiftKey)) {
      return;
    }

    if (key === 'Backspace' && !value.length) {
      const index = tags.length - 1;
      if (index >= 0) {
        this.onTagDelete({ index, id: tags[index].id });
      }

      clearTimeout(this._refreshTimeout);
      this._refreshTimeout = setTimeout(() => {
        this._refreshTimeout = undefined;
        this.onSuggestionsFetchRequested({ value: '' });
      });
      event.preventDefault();
    }

    if (delimiters.includes(key)) {
      const tag = resolveTag(value, this._autosuggestRef?.getHighlightedSuggestion(), suggestions, allowNew);
      if (tag) {
        this.addTag(tag);
        event.preventDefault();
      }
    }
  };

  onInputFocus = () => {
    this.setState({ isFocused: true });
  };

  onInputBlur = () => {
    this.setState({ isFocused: false });
    if (this.props.isDisabled || !this._autosuggestRef || !this.state.value) {
      return;
    }
    const { allowNew = true } = this.props;
    const { value, suggestions } = this.state;
    const tag = resolveTag(value, this._autosuggestRef.getHighlightedSuggestion(), suggestions, allowNew);
    if (tag) {
      this.addTag(tag);
    }
  };

  onSuggestionsFetchRequested = ({ value }: { value: string }) => {
    const lowerCaseValue = value.toLowerCase();
    const { tags, tagList, maxSuggestionsLength } = this.props;
    const suggestions = tagList.filter((tag) =>
      typeof tag.name === 'string' &&
      tag.name.toLowerCase().includes(lowerCaseValue) &&
      !tags.some((selected) => selected.id === tag.id)
    );
    this.setState({ suggestions: maxSuggestionsLength === undefined ? suggestions : suggestions.slice(0, maxSuggestionsLength) });
  };

  onSuggestionsClearRequested = () => {
    // Keep suggestions until the selection event has finished.
  };

  onSuggestionSelected = (_event: SyntheticEvent, { suggestion }: SuggestionSelectedEvent<RenderedTag<Id>>) => {
    this.addTag(suggestion);
  };

  renderInputComponent = (inputProps: RenderInputProps, forwardedRef: (element: HTMLElement | null) => void) => {
    const { tags, kind = kinds.INFO, canEdit = false, tagComponent = TagInputTag, isDisabled } = this.props;

    return (
      <TagInputInput
        forwardedRef={forwardedRef}
        tags={tags}
        kind={kind}
        inputProps={inputProps}
        isFocused={this.state.isFocused}
        canEdit={canEdit}
        isDisabled={isDisabled}
        tagComponent={tagComponent}
        onTagDelete={this.onTagDelete}
        onTagEdit={this.onTagEdit}
        onInputContainerPress={this.onInputContainerPress}
      />
    );
  };

  render() {
    const {
      className = styles.internalInput,
      inputContainerClassName = styles.input,
      hasError,
      hasWarning,
      placeholder = '',
      isDisabled,
      ...otherProps
    } = this.props;
    const { value, suggestions, isFocused } = this.state;

    return (
      <AutoSuggestInput
        {...otherProps}
        forwardedRef={this.setAutosuggestRef}
        className={className}
        inputContainerClassName={classNames(
          inputContainerClassName,
          isFocused && styles.isFocused,
          hasError && styles.hasError,
          hasWarning && styles.hasWarning
        )}
        value={value}
        placeholder={placeholder}
        isDisabled={isDisabled}
        suggestions={suggestions}
        getSuggestionValue={this.getSuggestionValue}
        shouldRenderSuggestions={this.shouldRenderSuggestions}
        focusInputOnSuggestionClick={false}
        renderSuggestion={this.renderSuggestion}
        renderInputComponent={this.renderInputComponent}
        onInputChange={this.onInputChange}
        onInputKeyDown={this.onInputKeyDown}
        onInputFocus={this.onInputFocus}
        onInputBlur={this.onInputBlur}
        onSuggestionSelected={this.onSuggestionSelected}
        onSuggestionsFetchRequested={this.onSuggestionsFetchRequested}
        onSuggestionsClearRequested={this.onSuggestionsClearRequested}
        onChange={({ value: nextValue }) => this.setState({ value: nextValue })}
      />
    );
  }
}

export default TagInput;
