import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { ValueChange } from 'Components/Form/formTypes';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { fetchNamingExamples, fetchNamingSettings, setNamingSettingsValue } from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import Naming from './Naming';
import { namingExamples } from './namingSettingsState';

const SECTION = 'naming';

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.advancedSettings === true,
    (state: LegacyRootState) => state.settings.namingExamples,
    createSettingsSectionSelector(SECTION),
    (advancedSettings, examplesState, sectionSettings) => {
      const examples = asStateRecord(examplesState);
      return {
        advancedSettings,
        examples: namingExamples(examples.item),
        examplesPopulated: examples.isPopulated === true,
        ...sectionSettings
      };
    }
  );
}

const mapDispatchToProps = {
  fetchNamingSettings,
  setNamingSettingsValue,
  fetchNamingExamples,
  clearPendingChanges
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = ConnectedProps<typeof connector>;

export class NamingConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount() {
    this.props.fetchNamingSettings();
    this.props.fetchNamingExamples();
  }

  componentWillUnmount() {
    clearTimeout(this._namingExampleTimeout);
    this._namingExampleTimeout = undefined;
    this.props.clearPendingChanges({ section: 'settings.naming' });
  }

  private _namingExampleTimeout: ReturnType<typeof setTimeout> | undefined = undefined;

  //
  // Control

  _fetchNamingExamples = () => {
    this._namingExampleTimeout = undefined;
    this.props.fetchNamingExamples();
  };

  //
  // Listeners

  onInputChange = ({ name, value }: ValueChange<unknown>) => {
    this.props.setNamingSettingsValue({ name, value });

    if (this._namingExampleTimeout) {
      clearTimeout(this._namingExampleTimeout);
    }

    this._namingExampleTimeout = setTimeout(this._fetchNamingExamples, 1000);
  };

  //
  // Render

  render() {
    return (
      <Naming
        onInputChange={this.onInputChange}
        {...this.props}
      />
    );
  }
}

export default connector(NamingConnector);
