import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { ValueChange } from 'Components/Form/formTypes';
import {
  saveDownloadClient,
  setDownloadClientFieldValue,
  setDownloadClientValue,
  testDownloadClient,
  toggleAdvancedSettings
} from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import { type LegacyRootState } from 'Store/StateTypes';
import EditDownloadClientModalContent from './EditDownloadClientModalContent';

export interface DownloadClientEditOwnProps {
  id?: number;
  onModalClose: () => void;
  onDeleteDownloadClientPress?: () => void;
  onSaveSuccess: () => void;
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.advancedSettings === true,
    createProviderSettingsSelector('downloadClients'),
    (advancedSettings, downloadClient) => {
      return {
        advancedSettings,
        ...downloadClient
      };
    }
  );
}

const mapDispatchToProps = {
  setDownloadClientValue: setDownloadClientValue as unknown as (change: ValueChange<unknown>) => { type: string; payload?: unknown },
  setDownloadClientFieldValue: setDownloadClientFieldValue as unknown as (change: ValueChange<unknown>) => { type: string; payload?: unknown },
  saveDownloadClient,
  testDownloadClient,
  toggleAdvancedSettings
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = DownloadClientEditOwnProps & ConnectedProps<typeof connector>;

export class EditDownloadClientModalContentConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidUpdate(prevProps: Props) {
    if (prevProps.isSaving && !this.props.isSaving && !this.props.saveError) {
      this.props.onSaveSuccess();
    }
  }

  //
  // Listeners

  onInputChange = ({ name, value }: ValueChange<unknown>) => {
    this.props.setDownloadClientValue({ name, value });
  };

  onFieldChange = ({ name, value }: ValueChange<unknown>) => {
    this.props.setDownloadClientFieldValue({ name, value });
  };

  onSavePress = () => {
    this.props.saveDownloadClient({ id: this.props.id });
  };

  onTestPress = () => {
    this.props.testDownloadClient({ id: this.props.id });
  };

  onAdvancedSettingsPress = () => {
    this.props.toggleAdvancedSettings();
  };

  //
  // Render

  render() {
    return (
      <EditDownloadClientModalContent
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

export default connector(EditDownloadClientModalContentConnector);
