import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { ValueChange } from 'Components/Form/formTypes';
import {
  saveNotification,
  setNotificationFieldValue,
  setNotificationValue,
  testNotification,
  toggleAdvancedSettings
} from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import { type LegacyRootState } from 'Store/StateTypes';
import EditNotificationModalContent from './EditNotificationModalContent';

export interface NotificationEditOwnProps {
  id?: number;
  onModalClose: () => void;
  onDeleteNotificationPress?: () => void;
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.advancedSettings === true,
    createProviderSettingsSelector('notifications'),
    (advancedSettings, notification) => {
      return {
        advancedSettings,
        ...notification
      };
    }
  );
}

const mapDispatchToProps = {
  setNotificationValue: setNotificationValue as unknown as (change: ValueChange<unknown>) => { type: string; payload?: unknown },
  setNotificationFieldValue: setNotificationFieldValue as unknown as (change: ValueChange<unknown>) => { type: string; payload?: unknown },
  saveNotification,
  testNotification,
  toggleAdvancedSettings
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = NotificationEditOwnProps & ConnectedProps<typeof connector>;

export class EditNotificationModalContentConnector extends Component<Props> {

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
    this.props.setNotificationValue({ name, value });
  };

  onFieldChange = ({ name, value }: ValueChange<unknown>) => {
    this.props.setNotificationFieldValue({ name, value });
  };

  onSavePress = () => {
    this.props.saveNotification({ id: this.props.id });
  };

  onTestPress = () => {
    this.props.testNotification({ id: this.props.id });
  };

  onAdvancedSettingsPress = () => {
    this.props.toggleAdvancedSettings();
  };

  //
  // Render

  render() {
    return (
      <EditNotificationModalContent
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

export default connector(EditNotificationModalContentConnector);
