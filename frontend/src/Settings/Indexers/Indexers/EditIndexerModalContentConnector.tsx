import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { ValueChange } from 'Components/Form/formTypes';
import { saveIndexer, setIndexerFieldValue, setIndexerValue, testIndexer, toggleAdvancedSettings } from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import { type LegacyRootState } from 'Store/StateTypes';
import EditIndexerModalContent from './EditIndexerModalContent';

export interface IndexerEditOwnProps {
  id?: number;
  onModalClose: () => void;
  onDeleteIndexerPress?: () => void;
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.advancedSettings === true,
    createProviderSettingsSelector('indexers'),
    (advancedSettings, indexer) => {
      return {
        advancedSettings,
        ...indexer
      };
    }
  );
}

const mapDispatchToProps = {
  setIndexerValue: setIndexerValue as unknown as (change: ValueChange<unknown>) => { type: string; payload?: unknown },
  setIndexerFieldValue: setIndexerFieldValue as unknown as (change: ValueChange<unknown>) => { type: string; payload?: unknown },
  saveIndexer,
  testIndexer,
  toggleAdvancedSettings
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = IndexerEditOwnProps & ConnectedProps<typeof connector>;

export class EditIndexerModalContentConnector extends Component<Props> {

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
    this.props.setIndexerValue({ name, value });
  };

  onFieldChange = ({ name, value }: ValueChange<unknown>) => {
    this.props.setIndexerFieldValue({ name, value });
  };

  onSavePress = () => {
    this.props.saveIndexer({ id: this.props.id });
  };

  onTestPress = () => {
    this.props.testIndexer({ id: this.props.id });
  };

  onAdvancedSettingsPress = () => {
    this.props.toggleAdvancedSettings();
  };

  //
  // Render

  render() {
    return (
      <EditIndexerModalContent
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

export default connector(EditIndexerModalContentConnector);
