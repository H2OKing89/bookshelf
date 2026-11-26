import { Component } from 'react';
import EditRootFolderModalConnector from 'Settings/MediaManagement/RootFolder/EditRootFolderModalConnector';
import EnhancedSelectInput from './EnhancedSelectInput';
import type { FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';
import RootFolderSelectInputOption from './RootFolderSelectInputOption';
import RootFolderSelectInputSelectedValue from './RootFolderSelectInputSelectedValue';

export interface RootFolderOption {
  key: string;
  value: string;
  name?: string;
  freeSpace?: number | null;
  isMissing?: boolean;
  isDisabled?: boolean;
  isHidden?: boolean;
}

export interface RootFolderSelectInputProps extends FormControlProps {
  name: string;
  value?: string | null;
  values: readonly RootFolderOption[];
  isSaving: boolean;
  saveError?: unknown;
  includeNoChange?: boolean;
  className?: string;
  hasError?: boolean;
  hasWarning?: boolean;
  isDisabled?: boolean;
  onChange: (change: ValueChange<string>) => void;
}

interface State {
  isAddNewRootFolderModalOpen: boolean;
  newRootFolderPath?: string;
}

class RootFolderSelectInput extends Component<RootFolderSelectInputProps, State> {

  //
  // Lifecycle

  constructor(props: RootFolderSelectInputProps) {
    super(props);

    this.state = {
      isAddNewRootFolderModalOpen: false
    };
  }

  componentDidUpdate(prevProps: RootFolderSelectInputProps) {
    const {
      name,
      isSaving,
      saveError,
      onChange
    } = this.props;

    const newRootFolderPath = this.state.newRootFolderPath;

    if (
      prevProps.isSaving &&
      !isSaving &&
      !saveError &&
      newRootFolderPath
    ) {
      onChange({ name, value: newRootFolderPath });
      this.setState({ newRootFolderPath: '' });
    }
  }

  //
  // Listeners

  onChange = ({ name, value }: ValueChange<string>) => {
    if (value === 'addNew') {
      this.setState({ isAddNewRootFolderModalOpen: true });
    } else {
      this.props.onChange({ name, value });
    }
  };

  onNewRootFolderSelect = ({ value }: ValueChange<string>) => {
    this.setState({ newRootFolderPath: value });
  };

  onAddRootFolderModalClose = () => {
    this.setState({ isAddNewRootFolderModalOpen: false });
  };

  //
  // Render

  render() {
    return (
      <div>
        <EnhancedSelectInput
          {...this.props}
          selectedValueComponent={RootFolderSelectInputSelectedValue}
          optionComponent={RootFolderSelectInputOption}
          onChange={this.onChange}
        />

        <EditRootFolderModalConnector
          isOpen={this.state.isAddNewRootFolderModalOpen}
          onModalClose={this.onAddRootFolderModalClose}
          onRootFolderAdded={this.onNewRootFolderSelect}
        />
      </div>
    );
  }
}

export default RootFolderSelectInput;
