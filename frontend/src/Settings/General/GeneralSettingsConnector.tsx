import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import type { ValueChange } from 'Components/Form/formTypes';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { fetchGeneralSettings, saveGeneralSettings, setGeneralSettingsValue } from 'Store/Actions/settingsActions';
import { restart } from 'Store/Actions/systemActions';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import createSystemStatusSelector from 'Store/Selectors/createSystemStatusSelector';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import { generalDrafts } from '../settingDrafts';
import GeneralSettings from './GeneralSettings';

const SECTION = 'general';

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.advancedSettings === true,
    createSettingsSectionSelector(SECTION),
    createCommandExecutingSelector(commandNames.RESET_API_KEY),
    createSystemStatusSelector(),
    (advancedSettings, sectionSettings, isResettingApiKey, status: unknown) => {
      const systemStatus = asStateRecord(status);

      return {
        advancedSettings,
        isResettingApiKey: isResettingApiKey === true,
        isWindows: systemStatus.isWindows === true,
        isWindowsService: systemStatus.isWindows === true && systemStatus.mode === 'service',
        mode: typeof systemStatus.mode === 'string' ? systemStatus.mode : '',
        packageUpdateMechanism: typeof systemStatus.packageUpdateMechanism === 'string' ? systemStatus.packageUpdateMechanism : '',
        ...sectionSettings
      };
    }
  );
}

const mapDispatchToProps = {
  setGeneralSettingsValue: setGeneralSettingsValue as unknown as (change: ValueChange<unknown>) => { type: string; payload?: unknown },
  saveGeneralSettings,
  fetchGeneralSettings,
  executeCommand,
  restart,
  clearPendingChanges
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = ConnectedProps<typeof connector>;

class GeneralSettingsConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount() {
    this.props.fetchGeneralSettings();
  }

  componentDidUpdate(prevProps: Props) {
    if (!this.props.isResettingApiKey && prevProps.isResettingApiKey) {
      this.props.fetchGeneralSettings();
    }
  }

  componentWillUnmount() {
    this.props.clearPendingChanges({ section: `settings.${SECTION}` });
  }

  //
  // Listeners

  onInputChange = ({ name, value }: ValueChange<unknown>) => {
    if (name === 'userIdentifier') {
      const account = generalDrafts(this.props.settings).accounts.value.find((item) => item.identifier === value);
      this.props.setGeneralSettingsValue({ name, value: account ? account.identifier : null });
      this.props.setGeneralSettingsValue({ name: 'username', value: account ? account.username : '' });
      this.props.setGeneralSettingsValue({ name: 'password', value: account && account.hasPassword ? '********' : '' });
      this.props.setGeneralSettingsValue({ name: 'passwordConfirmation', value: '' });
      return;
    }

    this.props.setGeneralSettingsValue({ name, value });
  };

  onSavePress = () => {
    this.props.saveGeneralSettings();
  };

  onConfirmResetApiKey = () => {
    this.props.executeCommand({ name: commandNames.RESET_API_KEY });
  };

  onConfirmRestart = () => {
    this.props.restart();
  };

  //
  // Render

  render() {
    return (
      <GeneralSettings
        onInputChange={this.onInputChange}
        onSavePress={this.onSavePress}
        onConfirmResetApiKey={this.onConfirmResetApiKey}
        onConfirmRestart={this.onConfirmRestart}
        {...this.props}
      />
    );
  }
}

export default connector(GeneralSettingsConnector);
