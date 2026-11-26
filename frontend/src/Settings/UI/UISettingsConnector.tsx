import React, { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { ValueChange } from 'Components/Form/formTypes';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { fetchUISettings, saveUISettings, setUISettingsValue } from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import UISettings from './UISettings';

const SECTION = 'ui';

function createLanguagesSelector() {
  return createSelector(
    (state: LegacyRootState) => state.settings.languages,
    (languages) => {
      const items = asStateRecord(languages).items;
      const filterItems = ['Any', 'Unknown'];

      if (!Array.isArray(items)) {
        return [];
      }

      return items.flatMap((entry: unknown) => {
        const item = asStateRecord(entry);
        return typeof item.id === 'number' && typeof item.name === 'string' &&
          !filterItems.includes(item.name) ? [{ key: item.id, value: item.name }] : [];
      });
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.advancedSettings === true,
    createSettingsSectionSelector(SECTION),
    createLanguagesSelector(),
    (advancedSettings, sectionSettings, languages) => {
      return {
        advancedSettings,
        languages,
        ...sectionSettings
      };
    }
  );
}

const mapDispatchToProps = {
  setUISettingsValue: setUISettingsValue as unknown as (change: ValueChange<unknown>) => { type: string; payload?: unknown },
  saveUISettings,
  fetchUISettings,
  clearPendingChanges
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = ConnectedProps<typeof connector>;

class UISettingsConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount() {
    this.props.fetchUISettings();
  }

  componentWillUnmount() {
    this.props.clearPendingChanges({ section: `settings.${SECTION}` });
  }

  //
  // Listeners

  onInputChange = ({ name, value }: ValueChange<unknown>) => {
    this.props.setUISettingsValue({ name, value });
  };

  onSavePress = () => {
    this.props.saveUISettings();
  };

  //
  // Render

  render() {
    return (
      <UISettings
        onInputChange={this.onInputChange}
        onSavePress={this.onSavePress}
        {...this.props}
      />
    );
  }
}

export default connector(UISettingsConnector);
