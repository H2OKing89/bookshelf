import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { deleteDownloadClient, fetchDownloadClients } from 'Store/Actions/settingsActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import { downloadClientCards, tagSummaries } from '../../providerCardState';
import DownloadClients from './DownloadClients';

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.downloadClients,
    (state: LegacyRootState) => state.tags.items,
    (value, tags) => {
      const state = asStateRecord(value);
      return {
        items: downloadClientCards(state.items),
        isFetching: state.isFetching === true,
        isPopulated: state.isPopulated === true,
        error: state.error,
        tagList: tagSummaries(tags)
      };
    }
  );
}

const mapDispatchToProps = {
  fetchDownloadClients,
  deleteDownloadClient
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = ConnectedProps<typeof connector>;

class DownloadClientsConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount() {
    this.props.fetchDownloadClients();
  }

  //
  // Listeners

  onConfirmDeleteDownloadClient = (id: number) => {
    this.props.deleteDownloadClient({ id });
  };

  //
  // Render

  render() {
    return (
      <DownloadClients
        {...this.props}
        onConfirmDeleteDownloadClient={this.onConfirmDeleteDownloadClient}
      />
    );
  }
}

export default connector(DownloadClientsConnector);
