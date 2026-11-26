import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { ValueChange } from 'Components/Form/formTypes';
import {
  saveImportList,
  setImportListFieldValue,
  setImportListValue,
  testImportList,
  toggleAdvancedSettings
} from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import { asStateRecord,type LegacyRootState } from 'Store/StateTypes';
import EditImportListModalContent from './EditImportListModalContent';

export interface ImportListEditOwnProps {
  id?: number;
  onModalClose: () => void;
  onDeleteImportListPress?: () => void;
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.advancedSettings === true,
    (state: LegacyRootState) => asStateRecord(state.settings.metadataProfiles),
    createProviderSettingsSelector('importLists'),
    (advancedSettings, metadataProfiles, importList) => {
      return {
        advancedSettings,
        showMetadataProfile: Array.isArray(metadataProfiles.items) && metadataProfiles.items.length > 1,
        ...importList
      };
    }
  );
}

const mapDispatchToProps = {
  setImportListValue,
  setImportListFieldValue,
  saveImportList,
  testImportList,
  toggleAdvancedSettings
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = ImportListEditOwnProps & ConnectedProps<typeof connector>;

export class EditImportListModalContentConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidUpdate(prevProps: Props) {
    if (prevProps.isSaving && !this.props.isSaving && !this.props.saveError) {
      this.props.onModalClose();
    }
  }

  //
  // Listeners

  onInputChange = ({ name, value }: ValueChange<unknown>) => {
    this.props.setImportListValue({ name, value });
  };

  onFieldChange = ({ name, value }: ValueChange<unknown>) => {
    this.props.setImportListFieldValue({ name, value });
  };

  onSavePress = () => {
    this.props.saveImportList({ id: this.props.id });
  };

  onTestPress = () => {
    this.props.testImportList({ id: this.props.id });
  };

  onAdvancedSettingsPress = () => {
    this.props.toggleAdvancedSettings();
  };

  //
  // Render

  render() {
    return (
      <EditImportListModalContent
        {...this.props}
        onSavePress={this.onSavePress}
        onTestPress={this.onTestPress}
        onAdvancedSettingsPress={this.onAdvancedSettingsPress}
        onInputChange={this.onInputChange}
        onFieldChange={this.onFieldChange}
      />
    );
  }
}

export default connector(EditImportListModalContentConnector);
