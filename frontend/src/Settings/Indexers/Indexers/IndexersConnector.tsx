import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { cloneIndexer, deleteIndexer, fetchIndexers } from 'Store/Actions/settingsActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import { indexerCards, tagSummaries } from '../../providerCardState';
import Indexers from './Indexers';

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.indexers,
    (state: LegacyRootState) => state.tags.items,
    (value, tags) => {
      const state = asStateRecord(value);
      return {
        items: indexerCards(state.items),
        isFetching: state.isFetching === true,
        isPopulated: state.isPopulated === true,
        error: state.error,
        tagList: tagSummaries(tags)
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchIndexers: fetchIndexers,
  dispatchDeleteIndexer: deleteIndexer,
  dispatchCloneIndexer: cloneIndexer
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = ConnectedProps<typeof connector>;

class IndexersConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount() {
    this.props.dispatchFetchIndexers();
  }

  //
  // Listeners

  onConfirmDeleteIndexer = (id: number) => {
    this.props.dispatchDeleteIndexer({ id });
  };

  //
  // Render

  render() {
    return (
      <Indexers
        {...this.props}
        onConfirmDeleteIndexer={this.onConfirmDeleteIndexer}
      />
    );
  }
}

export default connector(IndexersConnector);
