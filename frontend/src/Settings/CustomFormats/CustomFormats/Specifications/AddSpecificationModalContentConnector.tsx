import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { fetchCustomFormatSpecificationSchema, selectCustomFormatSpecificationSchema } from 'Store/Actions/settingsActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import { type SpecificationChoice, specificationSchemas } from '../../customFormatState';
import AddSpecificationModalContent from './AddSpecificationModalContent';

interface OwnProps { onModalClose: (result?: { specificationSelected?: boolean }) => void }

function createMapStateToProps() {
  return createSelector((state: LegacyRootState) => state.settings.customFormatSpecifications, (value) => {
    const section = asStateRecord(value);
    return { isSchemaFetching: section.isSchemaFetching === true, isSchemaPopulated: section.isSchemaPopulated === true, schemaError: section.schemaError, schema: specificationSchemas(section.schema) };
  });
}

const connector = connect(createMapStateToProps, { fetchCustomFormatSpecificationSchema, selectCustomFormatSpecificationSchema });
type Props = OwnProps & ConnectedProps<typeof connector>;

class AddSpecificationModalContentConnector extends Component<Props> {
  componentDidMount() {
    this.props.fetchCustomFormatSpecificationSchema();
  }

  onSpecificationSelect = ({ implementation, name }: SpecificationChoice) => {
    this.props.selectCustomFormatSpecificationSchema({ implementation, presetName: name });
    this.props.onModalClose({ specificationSelected: true });
  };

  render() {
    return <AddSpecificationModalContent {...this.props} onSpecificationSelect={this.onSpecificationSelect} />;
  }
}

export default connector(AddSpecificationModalContentConnector);
