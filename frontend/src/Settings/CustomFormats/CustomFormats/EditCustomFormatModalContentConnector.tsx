import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { ValueChange } from 'Components/Form/formTypes';
import { cloneCustomFormatSpecification, deleteCustomFormatSpecification, fetchCustomFormatSpecifications, saveCustomFormat, setCustomFormatValue } from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import { specificationCards } from '../customFormatState';
import EditCustomFormatModalContent from './EditCustomFormatModalContent';

interface OwnProps {
  id?: number;
  tagsFromId?: number;
  onModalClose: () => void;
  onDeleteCustomFormatPress?: () => void;
}

function createMapStateToProps() {
  return createSelector(
    createProviderSettingsSelector('customFormats'),
    (state: LegacyRootState) => state.settings.customFormatSpecifications,
    (format, value) => {
      const specifications = asStateRecord(value);
      return {
        ...format,
        error: format.error || specifications.error,
        specificationsPopulated: specifications.isPopulated === true,
        specifications: specificationCards(specifications.items)
      };
    }
  );
}

const connector = connect(createMapStateToProps, { setCustomFormatValue, saveCustomFormat, fetchCustomFormatSpecifications, cloneCustomFormatSpecification, deleteCustomFormatSpecification });
type Props = OwnProps & ConnectedProps<typeof connector>;

class EditCustomFormatModalContentConnector extends Component<Props> {
  componentDidMount() {
    this.props.fetchCustomFormatSpecifications({ id: this.props.tagsFromId ?? this.props.id });
  }

  componentDidUpdate(prevProps: Props) {
    if (prevProps.isSaving && !this.props.isSaving && !this.props.saveError) {
      this.props.onModalClose();
    }
  }

  onInputChange = (change: ValueChange<unknown>) => this.props.setCustomFormatValue(change);
  onSavePress = () => this.props.saveCustomFormat({ id: this.props.id });
  onCloneSpecificationPress = (id: number) => this.props.cloneCustomFormatSpecification({ id });
  onConfirmDeleteSpecification = (id: number) => this.props.deleteCustomFormatSpecification({ id });

  render() {
    return <EditCustomFormatModalContent {...this.props} onSavePress={this.onSavePress} onInputChange={this.onInputChange} onCloneSpecificationPress={this.onCloneSpecificationPress} onConfirmDeleteSpecification={this.onConfirmDeleteSpecification} />;
  }
}

export default connector(EditCustomFormatModalContentConnector);
