import React, { Component } from 'react';
import FileBrowserModal from 'Components/FileBrowser/FileBrowserModal';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import AutoSuggestInput from './AutoSuggestInput';
import type { FormControlProps } from './formControlProps';
import FormInputButton from './FormInputButton';
import type { ValueChange } from './formTypes';
import styles from './PathInput.css';

export interface PathSuggestion {
  path: string;
  type?: string;
}

export interface PathInputProps extends FormControlProps {
  className?: string;
  name: string;
  value?: string | null;
  paths: readonly PathSuggestion[];
  includeFiles: boolean;
  hasFileBrowser?: boolean;
  onChange: (change: ValueChange<string | null | undefined>) => void;
  onFetchPaths: (path: string) => void;
  onClearPaths: () => void;
}

interface State {
  value?: string | null;
  isFileBrowserModalOpen: boolean;
}

class PathInput extends Component<PathInputProps, State> {
  static defaultProps = {
    className: styles.inputWrapper,
    value: '',
    hasFileBrowser: true
  };

  //
  // Lifecycle

  constructor(props: PathInputProps) {
    super(props);

    this.state = {
      value: props.value,
      isFileBrowserModalOpen: false
    };
  }

  componentDidUpdate(prevProps: PathInputProps) {
    const { value } = this.props;

    if (prevProps.value !== value) {
      this.setState({ value });
    }
  }

  //
  // Control

  getSuggestionValue({ path }: PathSuggestion) {
    return path;
  }

  renderSuggestion({ path }: PathSuggestion, { query }: { query: string }) {
    const lastSeparatorIndex = Math.max(query.lastIndexOf('\\'), query.lastIndexOf('/'));

    if (lastSeparatorIndex === -1) {
      return (
        <span>{path}</span>
      );
    }

    return (
      <span>
        <span className={styles.pathMatch}>
          {path.slice(0, lastSeparatorIndex)}
        </span>
        {path.slice(lastSeparatorIndex)}
      </span>
    );
  }

  //
  // Listeners

  onInputChange = ({ value }: ValueChange<string>) => {
    this.setState({ value });
  };

  onInputKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    const path = this.props.paths[0];
    if (event.key !== 'Tab' || event.shiftKey || !path || path.path === this.state.value) {
      return;
    }

    event.preventDefault();
    this.props.onChange({
      name: this.props.name,
      value: path.path
    });

    if (path.type !== 'file') {
      this.props.onFetchPaths(path.path);
    }
  };

  onInputBlur = () => {
    this.props.onChange({
      name: this.props.name,
      value: this.state.value
    });

    this.props.onClearPaths();
  };

  onSuggestionsFetchRequested = ({ value }: { value: string }) => {
    this.props.onFetchPaths(value);
  };

  onSuggestionsClearRequested = () => {
    // Required because props aren't always rendered, but no-op
    // because we don't want to reset the paths after a path is selected.
  };

  onSuggestionSelected = (_event: React.SyntheticEvent, { suggestionValue }: { suggestionValue: string }) => {
    this.props.onFetchPaths(suggestionValue);
  };

  onFileBrowserOpenPress = () => {
    this.setState({ isFileBrowserModalOpen: true });
  };

  onFileBrowserModalClose = () => {
    this.setState({ isFileBrowserModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      className,
      name,
      paths,
      includeFiles,
      hasFileBrowser,
      onChange,
      ...otherProps
    } = this.props;

    const {
      value,
      isFileBrowserModalOpen
    } = this.state;

    return (
      <div className={className}>
        <AutoSuggestInput
          {...otherProps}
          className={hasFileBrowser ? styles.hasFileBrowser : undefined}
          name={name}
          value={value ?? ''}
          suggestions={paths}
          getSuggestionValue={this.getSuggestionValue}
          renderSuggestion={this.renderSuggestion}
          onInputKeyDown={this.onInputKeyDown}
          onInputBlur={this.onInputBlur}
          onSuggestionSelected={this.onSuggestionSelected}
          onSuggestionsFetchRequested={this.onSuggestionsFetchRequested}
          onSuggestionsClearRequested={this.onSuggestionsClearRequested}
          onChange={this.onInputChange}
        />

        {
          hasFileBrowser &&
            <div>
              <FormInputButton
                className={styles.fileBrowserButton}
                aria-label={translate(includeFiles ? 'BrowseFiles' : 'BrowseFolders')}
                onPress={this.onFileBrowserOpenPress}
              >
                <Icon name={icons.FOLDER_OPEN} aria-hidden={true} />
              </FormInputButton>

              <FileBrowserModal
                isOpen={isFileBrowserModalOpen}
                name={name}
                value={value ?? ''}
                includeFiles={includeFiles}
                onChange={onChange}
                onModalClose={this.onFileBrowserModalClose}
              />
            </div>
        }
      </div>
    );
  }
}

export default PathInput;
