import PropTypes from 'prop-types';
import { Component } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
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
import translate from 'Utilities/String/translate';
import toggleSelected from 'Utilities/Table/toggleSelected';
import QueueFilterButton from './QueueFilterButton';
import QueueOptions from './QueueOptions';
import { holdQueueRefresh, queueTarget, queueTargetsAreCurrent } from './queuePageQuery';
import QueueRowConnector from './QueueRowConnector';
import RemoveQueueItemModal from './RemoveQueueItemModal';
import SubmissionHolds from './SubmissionHolds';

class Queue extends Component {

  constructor(props, context) {
    super(props, context);

    this.state = {
      lastToggled: null,
      pageKey: props.pageKey,
      removal: null,
      selectedState: {}
    };
  }

  static getDerivedStateFromProps({ pageKey }, state) {
    return pageKey === state.pageKey ? null : { pageKey, lastToggled: null, selectedState: {} };
  }

  componentDidUpdate(prevProps) {
    const { items, canUseRowActions } = this.props;
    if (canUseRowActions && (items !== prevProps.items || !prevProps.canUseRowActions)) {
      this.setState((state) => {
        const previous = new Map(prevProps.items.map(({ id, revision }) => [id, revision]));
        const ids = new Set(items.filter((item) => (item.canRemove || item.canGrab) && previous.get(item.id) === item.revision).map(({ id }) => id));
        const selectedEntries = Object.entries(state.selectedState);
        const activeSelection = selectedEntries.filter(([id]) => ids.has(id));
        const lastToggled = ids.has(state.lastToggled) ? state.lastToggled : null;

        if (activeSelection.length !== selectedEntries.length || lastToggled !== state.lastToggled) {
          return { selectedState: Object.fromEntries(activeSelection), lastToggled };
        }

        return null;
      });
    }
  }

  componentWillUnmount() {
    this.releaseRemovalHold?.();
  }

  getSelectedItems() {
    return this.props.items.filter(({ id, canRemove, canGrab }) => (canRemove || canGrab) && this.state.selectedState[id]);
  }

  onSelectAllChange = ({ value }) => {
    if (this.props.canUseRowActions) {
      this.setState({
        lastToggled: null,
        selectedState: value ? Object.fromEntries(this.props.items.filter(({ canRemove, canGrab }) => canRemove || canGrab).map(({ id }) => [id, true])) : {}
      });
    }
  };

  onSelectedChange = ({ id, value, shiftKey = false }) => {
    const { canUseRowActions, items } = this.props;
    // TableSelectCell also calls this when mounting or unmounting.
    if (!canUseRowActions || value == null || !items.some((item) => item.id === id && (item.canRemove || item.canGrab))) {
      return;
    }

    this.setState((state) => (value === !!state.selectedState[id] ?
      null : toggleSelected(state, items.filter(({ canRemove, canGrab }) => canRemove || canGrab), id, value, shiftKey)));
  };

  grab(items, bulk) {
    if (this.props.canUseRowActions && items.length && items.every(({ canGrab }) => canGrab)) {
      this.props.onQueueAction({ kind: 'grab', bulk, pageKey: this.props.pageKey, targets: items.map(queueTarget) });
    }
  }

  onGrabSelectedPress = () => {
    const items = this.getSelectedItems();
    if (items.every(({ canGrab }) => canGrab)) {
      this.grab(items, true);
    }
  };

  onGrabRowPress = (id) => {
    this.grab(this.props.items.filter((item) => item.id === id), false);
  };

  openRemoval(items, bulk) {
    if (!this.props.canUseRowActions || !items.length || !items.every(({ canRemove }) => canRemove) || this.releaseRemovalHold) {
      return;
    }

    this.releaseRemovalHold = holdQueueRefresh();
    this.setState({
      removal: {
        bulk,
        pageKey: this.props.pageKey,
        targets: items.map(queueTarget),
        sourceTitle: bulk ? undefined : items[0].title,
        canChangeCategory: items.every((item) => item.kind === 'pending' || item.canChangeCategory),
        canRemoveFromClient: items.every((item) => item.kind === 'pending' || item.canRemoveFromClient),
        canIgnore: items.every((item) => item.kind === 'pending' || item.canIgnore),
        canBlocklist: items.every((item) => item.canBlocklist),
        canRetry: items.every((item) => item.kind === 'pending' || item.canRetry),
        hasPending: items.some(({ kind }) => kind === 'pending'),
        isPending: items.every(({ kind }) => kind === 'pending')
      }
    });
  }

  onRemoveSelectedPress = () => {
    this.openRemoval(this.getSelectedItems(), true);
  };

  onRemoveRowPress = (id) => {
    this.openRemoval(this.props.items.filter((item) => item.id === id), false);
  };

  onRemoveConfirmed = (options) => {
    const { removal } = this.state;
    if (removal) {
      const { bulk, pageKey, targets } = removal;
      this.props.onQueueAction({ kind: 'remove', options, bulk, pageKey, targets });
    }
    this.onRemovalClose();
  };

  onRemovalClose = () => {
    this.releaseRemovalHold?.();
    this.releaseRemovalHold = undefined;
    this.setState({ removal: null });
  };

  render() {
    const {
      actionError,
      canUseRowActions,
      isFetching,
      isPopulated,
      error,
      columns,
      items,
      hasQueueFilters,
      pageKey,
      pendingAction,
      totalRecords,
      isRefreshMonitoredDownloadsExecuting,
      onRefreshPress,
      onQueueAction,
      ...otherProps
    } = this.props;

    const { removal, selectedState } = this.state;
    const isRefreshing = isFetching || isRefreshMonitoredDownloadsExecuting;
    const isAllPopulated = isPopulated;
    const hasError = error;
    const selectedItems = this.getSelectedItems();
    const selectedCount = selectedItems.length;
    const canActOnSelected = canUseRowActions && selectedCount > 0;
    const pendingIds = new Set(pendingAction?.targets.map(({ id }) => id));
    const confirmationIsStale = !!removal && (!canUseRowActions || pageKey !== removal.pageKey ||
      !queueTargetsAreCurrent(items, removal.targets));

    return (
      <PageContent title={translate('Queue')}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('Refresh')}
              iconName={icons.REFRESH}
              isSpinning={isRefreshing}
              isDisabled={!!pendingAction}
              onPress={onRefreshPress}
            />

            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('GrabSelected')}
              iconName={icons.DOWNLOAD}
              isDisabled={!canActOnSelected || !selectedItems.every(({ canGrab }) => canGrab)}
              isSpinning={pendingAction?.kind === 'grab' && pendingAction.bulk}
              onPress={this.onGrabSelectedPress}
            />

            <PageToolbarButton
              label={translate('RemoveSelected')}
              iconName={icons.REMOVE}
              isDisabled={!canActOnSelected || !selectedItems.every(({ canRemove }) => canRemove)}
              isSpinning={pendingAction?.kind === 'remove' && pendingAction.bulk}
              onPress={this.onRemoveSelectedPress}
            />
          </PageToolbarSection>

          <PageToolbarSection alignContent={align.RIGHT}>
            <TableOptionsModalWrapper
              columns={columns}
              {...otherProps}
              optionsComponent={QueueOptions}
            >
              <PageToolbarButton
                label={translate('Options')}
                iconName={icons.TABLE}
              />
            </TableOptionsModalWrapper>

            <QueueFilterButton />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody>
          <SubmissionHolds isRefreshing={isRefreshing} />
          {isRefreshing && !isAllPopulated && <LoadingIndicator />}
          {
            !isRefreshing && hasError &&
              <Alert kind={kinds.DANGER}>{translate('FailedToLoadQueue')}</Alert>
          }
          {
            actionError &&
              <Alert kind={kinds.DANGER} role="alert">{translate('QueueActionFailed')}</Alert>
          }
          {
            isAllPopulated && canUseRowActions && !hasError && !items.length &&
              <Alert kind={kinds.INFO}>{translate(hasQueueFilters ? 'QueueNoMatches' : 'QueueIsEmpty')}</Alert>
          }

          {
            isAllPopulated && !!items.length &&
              <div>
                <Table
                  columns={columns}
                  selectAll={true}
                  allSelected={selectedCount > 0 && selectedCount === items.filter(({ canRemove, canGrab }) => canRemove || canGrab).length}
                  allUnselected={selectedCount === 0}
                  isSelectAllDisabled={!canUseRowActions || !items.some(({ canRemove, canGrab }) => canRemove || canGrab)}
                  {...otherProps}
                  optionsComponent={QueueOptions}
                  onSelectAllChange={this.onSelectAllChange}
                >
                  <TableBody>
                    {
                      items.map((item) => (
                        <QueueRowConnector
                          key={item.id}
                          columns={columns}
                          {...item}
                          isSelected={!!selectedState[item.id]}
                          canUseRowActions={canUseRowActions}
                          isGrabbing={pendingAction?.kind === 'grab' && pendingIds.has(item.id)}
                          isRemoving={pendingAction?.kind === 'remove' && pendingIds.has(item.id)}
                          onSelectedChange={this.onSelectedChange}
                          onGrabPress={this.onGrabRowPress}
                          onRemoveQueueItemPress={this.onRemoveRowPress}
                        />
                      ))
                    }
                  </TableBody>
                </Table>

                <TablePager
                  totalRecords={totalRecords}
                  isFetching={isRefreshing}
                  {...otherProps}
                />
              </div>
          }
        </PageContentBody>

        <RemoveQueueItemModal
          isOpen={!!removal}
          sourceTitle={removal?.sourceTitle}
          selectedCount={removal?.bulk ? removal.targets.length : undefined}
          canChangeCategory={!!removal?.canChangeCategory}
          canIgnore={!!removal?.canIgnore}
          canRemoveFromClient={!!removal?.canRemoveFromClient}
          canBlocklist={!!removal?.canBlocklist}
          canRetry={!!removal?.canRetry}
          hasPending={!!removal?.hasPending}
          isPending={!!removal?.isPending}
          isRemoveDisabled={confirmationIsStale}
          onRemovePress={this.onRemoveConfirmed}
          onModalClose={this.onRemovalClose}
        />
      </PageContent>
    );
  }
}

Queue.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  hasQueueFilters: PropTypes.bool.isRequired,
  pageKey: PropTypes.string.isRequired,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  totalRecords: PropTypes.number,
  canUseRowActions: PropTypes.bool.isRequired,
  actionError: PropTypes.bool.isRequired,
  pendingAction: PropTypes.object,
  isRefreshMonitoredDownloadsExecuting: PropTypes.bool.isRequired,
  onRefreshPress: PropTypes.func.isRequired,
  onQueueAction: PropTypes.func.isRequired
};

export default Queue;
