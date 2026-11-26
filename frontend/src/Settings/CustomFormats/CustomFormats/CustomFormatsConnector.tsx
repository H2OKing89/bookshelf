import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { cloneCustomFormat, deleteCustomFormat, fetchCustomFormats } from 'Store/Actions/settingsActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import sortByName from 'Utilities/Array/sortByName';
import { formatCards } from '../customFormatState';
import CustomFormats from './CustomFormats';

function createMapStateToProps() {
  return createSelector((state: LegacyRootState) => state.settings.customFormats, (value) => {
    const section = asStateRecord(value);
    return {
      items: formatCards(section.items).sort(sortByName),
      isFetching: section.isFetching === true,
      isPopulated: section.isPopulated === true,
      isDeleting: section.isDeleting === true,
      error: section.error
    };
  });
}

const connector = connect(createMapStateToProps, { fetchCustomFormats, deleteCustomFormat, cloneCustomFormat });
type Props = ConnectedProps<typeof connector>;

class CustomFormatsConnector extends Component<Props> {
  componentDidMount() {
    this.props.fetchCustomFormats();
  }

  onConfirmDeleteCustomFormat = (id: number) => this.props.deleteCustomFormat({ id });
  onCloneCustomFormatPress = (id: number) => this.props.cloneCustomFormat({ id });

  render() {
    return <CustomFormats {...this.props} onConfirmDeleteCustomFormat={this.onConfirmDeleteCustomFormat} onCloneCustomFormatPress={this.onCloneCustomFormatPress} />;
  }
}

export default connector(CustomFormatsConnector);
