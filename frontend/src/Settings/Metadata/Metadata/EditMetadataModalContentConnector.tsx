import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import type { ValueChange } from 'Components/Form/formTypes';
import { saveMetadata, setMetadataFieldValue, setMetadataValue } from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import EditMetadataModalContent from './EditMetadataModalContent';

export interface MetadataEditOwnProps {
  id?: number;
  onModalClose: () => void;
  onDeleteMetadataPress?: () => void;
}

function createMapStateToProps() {
  return createProviderSettingsSelector('metadata');
}

const mapDispatchToProps = {
  setMetadataValue,
  setMetadataFieldValue,
  saveMetadata
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = MetadataEditOwnProps & ConnectedProps<typeof connector>;

export class EditMetadataModalContentConnector extends Component<Props> {

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
    this.props.setMetadataValue({ name, value });
  };

  onFieldChange = ({ name, value }: ValueChange<unknown>) => {
    this.props.setMetadataFieldValue({ name, value });
  };

  onSavePress = () => {
    this.props.saveMetadata({ id: this.props.id });
  };

  //
  // Render

  render() {
    return (
      <EditMetadataModalContent
        {...this.props}
        onSavePress={this.onSavePress}
        onInputChange={this.onInputChange}
        onFieldChange={this.onFieldChange}
      />
    );
  }
}

export default connector(EditMetadataModalContentConnector);
