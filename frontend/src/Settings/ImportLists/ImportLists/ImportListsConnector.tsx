import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { deleteImportList, fetchImportLists, fetchRootFolders } from 'Store/Actions/settingsActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import { importListCards } from '../../providerCardState';
import ImportLists from './ImportLists';

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.importLists,
    (value) => {
      const state = asStateRecord(value);
      return {
        items: importListCards(state.items),
        isFetching: state.isFetching === true,
        isPopulated: state.isPopulated === true,
        error: state.error
      };
    }
  );
}

const mapDispatchToProps = {
  fetchImportLists,
  deleteImportList,
  fetchRootFolders
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = ConnectedProps<typeof connector>;

class ListsConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount() {
    this.props.fetchImportLists();
    this.props.fetchRootFolders();
  }

  //
  // Listeners

  onConfirmDeleteImportList = (id: number) => {
    this.props.deleteImportList({ id });
  };

  //
  // Render

  render() {
    return (
      <ImportLists
        {...this.props}
        onConfirmDeleteImportList={this.onConfirmDeleteImportList}
      />
    );
  }
}

export default connector(ListsConnector);
