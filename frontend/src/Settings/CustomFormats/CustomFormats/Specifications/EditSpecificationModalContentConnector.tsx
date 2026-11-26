import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { ValueChange } from 'Components/Form/formTypes';
import { clearCustomFormatSpecificationPending, saveCustomFormatSpecification, setCustomFormatSpecificationFieldValue, setCustomFormatSpecificationValue } from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import type { LegacyRootState } from 'Store/StateTypes';
import EditSpecificationModalContent from './EditSpecificationModalContent';

interface OwnProps {
  id?: number;
  onModalClose: () => void;
  onDeleteSpecificationPress?: () => void;
}

function createMapStateToProps() {
  return createSelector((state: LegacyRootState) => state.settings.advancedSettings === true, createProviderSettingsSelector('customFormatSpecifications'), (advancedSettings, specification) => ({ advancedSettings, ...specification }));
}

const connector = connect(createMapStateToProps, { setCustomFormatSpecificationValue, setCustomFormatSpecificationFieldValue, clearCustomFormatSpecificationPending, saveCustomFormatSpecification });
type Props = OwnProps & ConnectedProps<typeof connector>;

class EditSpecificationModalContentConnector extends Component<Props> {
  onInputChange = (change: ValueChange<unknown>) => this.props.setCustomFormatSpecificationValue(change);
  onFieldChange = (change: ValueChange<unknown>) => this.props.setCustomFormatSpecificationFieldValue(change);
  onCancelPress = () => {
    this.props.clearCustomFormatSpecificationPending();
    this.props.onModalClose();
  };
  onSavePress = () => {
    if (this.props.saveCustomFormatSpecification({ id: this.props.id }) !== false) {
      this.props.onModalClose();
    }
  };

  render() {
    return <EditSpecificationModalContent {...this.props} onCancelPress={this.onCancelPress} onSavePress={this.onSavePress} onInputChange={this.onInputChange} onFieldChange={this.onFieldChange} />;
  }
}

export default connector(EditSpecificationModalContentConnector);
