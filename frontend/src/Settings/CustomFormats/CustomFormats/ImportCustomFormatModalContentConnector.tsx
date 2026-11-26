import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { batchActions } from 'redux-batched-actions';
import { createSelector } from 'reselect';
import { set, update } from 'Store/Actions/baseActions';
import { fetchCustomFormatSpecificationSchema } from 'Store/Actions/settingsActions';
import { asStateRecord, type LegacyDispatch, type LegacyRootState } from 'Store/StateTypes';
import { type ImportDraft, parseCustomFormatImport } from '../customFormatState';
import ImportCustomFormatModalContent from './ImportCustomFormatModalContent';

interface OwnProps { onModalClose: () => void }

function createMapStateToProps() {
  return createSelector((state: LegacyRootState) => state.settings.customFormatSpecifications, (value) => {
    const section = asStateRecord(value);
    return {
      isFetching: section.isSchemaFetching === true,
      specificationsPopulated: section.isSchemaPopulated === true,
      error: section.schemaError,
      specificationSchema: section.schema
    };
  });
}

function mapDispatchToProps(dispatch: LegacyDispatch) {
  return {
    fetchCustomFormatSpecificationSchema: () => dispatch(fetchCustomFormatSpecificationSchema()),
    applyImport(draft: ImportDraft) {
      // Only a fully parsed draft can replace the current editor. No partial
      // conditions are visible, and a failed parse leaves the old draft intact.
      dispatch(batchActions([
        set({ section: 'settings.customFormats', pendingChanges: draft.pendingChanges, saveError: null }),
        update({ section: 'settings.customFormatSpecifications', data: draft.specifications }),
        set({ section: 'settings.customFormatSpecifications', pendingChanges: {}, selectedSchema: {}, isPopulated: true, error: null, saveError: null })
      ]));
    }
  };
}

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class ImportCustomFormatModalContentConnector extends Component<Props> {
  componentDidMount() {
    this.props.fetchCustomFormatSpecificationSchema();
  }

  onImportPress = (text: string) => {
    try {
      this.props.applyImport(parseCustomFormatImport(text, this.props.specificationSchema));
      return null;
    } catch (error) {
      return { message: error instanceof Error ? error.message : 'Invalid custom format JSON' };
    }
  };

  render() {
    return <ImportCustomFormatModalContent {...this.props} onImportPress={this.onImportPress} />;
  }
}

export default connector(ImportCustomFormatModalContentConnector);
