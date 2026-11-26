import _ from 'lodash';
import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { ValueChange } from 'Components/Form/formTypes';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { fetchMediaManagementSettings, saveMediaManagementSettings, saveNamingSettings, setMediaManagementSettingsValue } from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import createSystemStatusSelector from 'Store/Selectors/createSystemStatusSelector';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import MediaManagement from './MediaManagement';

const SECTION = 'mediaManagement';

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.advancedSettings === true,
    (state: LegacyRootState) => asStateRecord(state.settings.naming),
    createSettingsSectionSelector(SECTION),
    createSystemStatusSelector(),
    (advancedSettings, namingSettings, sectionSettings, status: unknown) => {
      const systemStatus = asStateRecord(status);

      return {
        advancedSettings,
        ...sectionSettings,
        hasPendingChanges: !_.isEmpty(namingSettings.pendingChanges) || sectionSettings.hasPendingChanges,
        isWindows: systemStatus.isWindows === true
      };
    }
  );
}

const mapDispatchToProps = {
  fetchMediaManagementSettings,
  setMediaManagementSettingsValue: setMediaManagementSettingsValue as unknown as (change: ValueChange<unknown>) => { type: string; payload?: unknown },
  saveMediaManagementSettings,
  saveNamingSettings,
  clearPendingChanges
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = ConnectedProps<typeof connector>;

class MediaManagementConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount() {
    this.props.fetchMediaManagementSettings();
  }

  componentWillUnmount() {
    this.props.clearPendingChanges({ section: `settings.${SECTION}` });
  }

  //
  // Listeners

  onInputChange = ({ name, value }: ValueChange<unknown>) => {
    this.props.setMediaManagementSettingsValue({ name, value });
  };

  onSavePress = () => {
    this.props.saveMediaManagementSettings();
    this.props.saveNamingSettings();
  };

  //
  // Render

  render() {
    return (
      <MediaManagement
        onInputChange={this.onInputChange}
        onSavePress={this.onSavePress}
        {...this.props}
      />
    );
  }
}

export default connector(MediaManagementConnector);
