import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import FilterMenu from 'Components/Menu/FilterMenu';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import TablePager from 'Components/Table/TablePager';
import { align, icons, kinds } from 'Helpers/Props';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';
import getFilterValue from 'Utilities/Filter/getFilterValue';
import translate from 'Utilities/String/translate';
import { emptyWantedSelection, reconcileWantedSelection, selectAllWantedBooks, toggleWantedBook } from '../wantedSelection';
import MissingRowConnector from './MissingRowConnector';

function getMonitoredValue(props) {
  const {
    filters,
    selectedFilterKey
  } = props;

  return getFilterValue(filters, selectedFilterKey, 'monitored', false);
}

class Missing extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      selection: emptyWantedSelection,
      isConfirmSearchAllMissingModalOpen: false,
      isInteractiveImportModalOpen: false
    };
  }

  static getDerivedStateFromProps(props, state) {
    const selection = reconcileWantedSelection(state.selection, props.selectionScope, props.items);
    if (selection !== state.selection || (!props.canUseRowActions && state.isConfirmSearchAllMissingModalOpen)) {
      return {
        selection,
        isConfirmSearchAllMissingModalOpen: false
      };
    }

    return null;
  }

  //
  // Control

  getSelectedIds = () => {
    return this.props.canUseRowActions ? [...this.state.selection.selectedIds].sort((left, right) => left - right) : [];
  };

  //
  // Listeners

  onSelectAllChange = ({ value }) => {
    if (this.props.canUseRowActions) {
      this.setState(({ selection }) => ({ selection: selectAllWantedBooks(selection, value) }));
    }
  };

  onSelectedChange = ({ id, value, shiftKey = false }) => {
    if (this.props.canUseRowActions && value != null) {
      this.setState(({ selection }) => {
        const nextSelection = toggleWantedBook(selection, id, value, shiftKey);
        return nextSelection === selection ? null : { selection: nextSelection };
      });
    }
  };

  onSearchSelectedPress = () => {
    const selected = this.getSelectedIds();

    this.props.onSearchSelectedPress(selected);
  };

  onToggleSelectedPress = () => {
    this.props.onToggleSelectedPress(this.getSelectedIds());
  };

  onSearchAllMissingPress = () => {
    this.setState({ isConfirmSearchAllMissingModalOpen: true });
  };

  onSearchAllMissingConfirmed = () => {
    this.props.onSearchAllPress();
    this.setState({ isConfirmSearchAllMissingModalOpen: false });
  };

  onConfirmSearchAllMissingModalClose = () => {
    this.setState({ isConfirmSearchAllMissingModalOpen: false });
  };

  onInteractiveImportPress = () => {
    this.setState({ isInteractiveImportModalOpen: true });
  };

  onInteractiveImportModalClose = () => {
    this.setState({ isInteractiveImportModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      isFetching,
      isPopulated,
      error,
      items,
      isAuthorFetching,
      isAuthorPopulated,
      selectedFilterKey,
      filters,
      columns,
      totalRecords,
      isSearching,
      isSaving,
      canUseRowActions,
      selectionScope,
      queuedBookIds,
      fileQualities,
      monitorError,
      onFilterSelect,
      ...otherProps
    } = this.props;

    const {
      selection,
      isConfirmSearchAllMissingModalOpen,
      isInteractiveImportModalOpen
    } = this.state;

    const isAllPopulated = isPopulated && isAuthorPopulated;
    const isAnyFetching = isFetching || isAuthorFetching;

    const visibleSelectedCount = selectionScope === null ? 0 : selection.selectedIds.size;
    const itemsSelected = canUseRowActions && visibleSelectedCount > 0;
    const isShowingMonitored = getMonitoredValue(this.props);

    return (
      <PageContent title={translate('Missing')}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('SearchSelected')}
              iconName={icons.SEARCH}
              isDisabled={!itemsSelected || isSearching}
              onPress={this.onSearchSelectedPress}
            />

            <PageToolbarButton
              label={isShowingMonitored ? translate('IsShowingMonitoredUnmonitorSelected') : translate('IsShowingMonitoredMonitorSelected')}
              iconName={isShowingMonitored ? icons.UNMONITORED : icons.MONITORED}
              isDisabled={!itemsSelected || isSaving}
              isSpinning={isSaving}
              onPress={this.onToggleSelectedPress}
            />

            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('SearchAll')}
              iconName={icons.SEARCH}
              isDisabled={!canUseRowActions || !items.length}
              isSpinning={isSearching}
              onPress={this.onSearchAllMissingPress}
            />

            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('ManualImport')}
              iconName={icons.INTERACTIVE}
              onPress={this.onInteractiveImportPress}
            />

          </PageToolbarSection>

          <PageToolbarSection alignContent={align.RIGHT}>
            <TableOptionsModalWrapper
              {...otherProps}
              columns={columns}
            >
              <PageToolbarButton
                label={translate('Options')}
                iconName={icons.TABLE}
              />
            </TableOptionsModalWrapper>

            <FilterMenu
              alignMenu={align.RIGHT}
              selectedFilterKey={selectedFilterKey}
              filters={filters}
              customFilters={[]}
              onFilterSelect={onFilterSelect}
            />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody>
          {
            isAnyFetching && !isAllPopulated &&
              <LoadingIndicator />
          }

          {
            !isAnyFetching && error &&
              <Alert kind={kinds.DANGER}>
                Error fetching missing items
              </Alert>
          }

          {
            monitorError &&
              <Alert kind={kinds.DANGER}>
                Could not change monitoring for the selected books.
              </Alert>
          }

          {
            isAllPopulated && !error && !items.length &&
              <Alert kind={kinds.INFO}>
                {translate('NoMissingItems')}
              </Alert>
          }

          {
            isAllPopulated && !error && !!items.length &&
              <div>
                <Table
                  columns={columns}
                  selectAll={true}
                  isSelectAllDisabled={!canUseRowActions}
                  allSelected={visibleSelectedCount > 0 && visibleSelectedCount === selection.bookIds.length}
                  allUnselected={visibleSelectedCount === 0}
                  {...otherProps}
                  onSelectAllChange={this.onSelectAllChange}
                >
                  <TableBody>
                    {
                      items.map((item) => {
                        return (
                          <MissingRowConnector
                            key={item.id}
                            isSelected={selectionScope !== null && selection.selectedIds.has(item.id)}
                            columns={columns}
                            {...item}
                            canUseRowActions={canUseRowActions}
                            isQueued={queuedBookIds.includes(item.id)}
                            qualities={fileQualities[item.id]}
                            onSelectedChange={this.onSelectedChange}
                          />
                        );
                      })
                    }
                  </TableBody>
                </Table>

                <TablePager
                  totalRecords={totalRecords}
                  isFetching={isFetching}
                  {...otherProps}
                />

                <ConfirmModal
                  isOpen={isConfirmSearchAllMissingModalOpen}
                  kind={kinds.DANGER}
                  title={translate('SearchForAllMissingBooks')}
                  message={
                    <div>
                      <div>
                        Are you sure you want to search for all {totalRecords} missing books?
                      </div>
                      <div>
                        This cannot be cancelled once started without restarting Readarr.
                      </div>
                    </div>
                  }
                  confirmLabel={translate('Search')}
                  onConfirm={this.onSearchAllMissingConfirmed}
                  onCancel={this.onConfirmSearchAllMissingModalClose}
                />
              </div>
          }

          <InteractiveImportModal
            isOpen={isInteractiveImportModalOpen}
            onModalClose={this.onInteractiveImportModalClose}
            showReplaceExistingFiles={true}
          />
        </PageContentBody>
      </PageContent>
    );
  }
}

Missing.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  isAuthorFetching: PropTypes.bool.isRequired,
  isAuthorPopulated: PropTypes.bool.isRequired,
  selectedFilterKey: PropTypes.string.isRequired,
  filters: PropTypes.arrayOf(PropTypes.object).isRequired,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  totalRecords: PropTypes.number,
  isSearching: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  onFilterSelect: PropTypes.func.isRequired,
  onSearchSelectedPress: PropTypes.func.isRequired,
  canUseRowActions: PropTypes.bool.isRequired,
  selectionScope: PropTypes.string,
  queuedBookIds: PropTypes.arrayOf(PropTypes.number).isRequired,
  fileQualities: PropTypes.object.isRequired,
  monitorError: PropTypes.object,
  onToggleSelectedPress: PropTypes.func.isRequired,
  onSearchAllPress: PropTypes.func.isRequired
};

export default Missing;
