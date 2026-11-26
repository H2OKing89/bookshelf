import { Component } from 'react';
import type { BookFileId } from 'Book/BookIdentity';
import type { LibraryBookFile } from 'Book/LibraryModels';
import Alert from 'Components/Alert';
import type { SelectOption, ValueChange } from 'Components/Form/formTypes';
import SelectInput from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import type Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import type { CheckboxSelectionChange, TableSelectionChange } from 'Components/Table/tableSelection';
import { kinds } from 'Helpers/Props';
import type { SortDirection } from 'Helpers/Props/sortDirections';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import getRemovedItems from 'Utilities/Object/getRemovedItems';
import hasDifferentItems from 'Utilities/Object/hasDifferentItems';
import translate from 'Utilities/String/translate';
import removeOldSelectedState from 'Utilities/Table/removeOldSelectedState';
import selectAll from 'Utilities/Table/selectAll';
import type { SelectedState, SelectionFlags, SelectionState } from 'Utilities/Table/selectionTypes';
import toggleSelected from 'Utilities/Table/toggleSelected';
import BookFileEditorRow from './BookFileEditorRow';
import { type QualityChoice,selectedFileIds } from './bookFileEditorState';
import styles from './BookFileEditorTableContent.css';

export interface FileEditorProps {
  items: readonly LibraryBookFile[];
  columns: readonly Column[];
  qualities: readonly QualityChoice[];
  sortKey?: string;
  sortDirection?: SortDirection;
  isDeleting: boolean;
  isSaving: boolean;
  saveError?: unknown;
  isFetching: boolean;
  isPopulated: boolean;
  error?: unknown;
  onSortPress: (key: string) => void;
  onDeletePress: (ids: BookFileId[]) => void;
  onQualityChange: (ids: BookFileId[], qualityId: number) => void;
  onReleaseGroupChange: (ids: BookFileId[], releaseGroup: string) => void;
  dispatchDeleteBookFile: (options: { id: BookFileId }) => void;
}

interface State extends SelectionState<BookFileId>, SelectionFlags {
  releaseGroup: string;
  isConfirmDeleteModalOpen: boolean;
}

class BookFileEditorTableContent extends Component<FileEditorProps, State> {

  //
  // Lifecycle

  constructor(props: FileEditorProps) {
    super(props);

    this.state = {
      allSelected: false,
      allUnselected: false,
      lastToggled: null,
      selectedState: {},
      releaseGroup: '',
      isConfirmDeleteModalOpen: false
    };
  }

  componentDidUpdate(prevProps: FileEditorProps) {
    if (hasDifferentItems(prevProps.items, this.props.items)) {
      this.setState((state) => {
        return {
          ...removeOldSelectedState(state, getRemovedItems(prevProps.items, this.props.items)),
          lastToggled: this.props.items.some((item) => item.id === state.lastToggled) ? state.lastToggled : null
        };
      });
    }
  }

  //
  // Control

  getSelectedIds = () => {
    return selectedFileIds(this.props.items, this.state.selectedState);
  };

  getSelectedReleaseGroup = (selectedState: SelectedState) => {
    const selectedGroups = this.props.items
      .filter((item) => selectedState[item.id])
      .map((item) => item.releaseGroup || '');

    return selectedGroups.length && selectedGroups.every((group) => group === selectedGroups[0]) ?
      selectedGroups[0] : '';
  };

  //
  // Listeners

  onSelectAllChange = ({ value }: CheckboxSelectionChange) => {
    const selectedState: Record<string, boolean> = {};
    this.props.items.forEach((item) => {
      selectedState[item.id] = false;
    });
    const selection = selectAll(selectedState, value);
    this.setState({ ...selection, releaseGroup: this.getSelectedReleaseGroup(selection.selectedState) });
  };

  onSelectedChange = ({ id, value, shiftKey = false }: TableSelectionChange<BookFileId>) => {
    this.setState((state) => {
      const selection = toggleSelected(state, this.props.items, id, value, shiftKey);
      return { ...selection, releaseGroup: this.getSelectedReleaseGroup(selection.selectedState) };
    });
  };

  onDeletePress = () => {
    this.setState({ isConfirmDeleteModalOpen: true });
  };

  onConfirmDelete = () => {
    this.setState({ isConfirmDeleteModalOpen: false });
    this.props.onDeletePress(this.getSelectedIds());
  };

  onConfirmDeleteModalClose = () => {
    this.setState({ isConfirmDeleteModalOpen: false });
  };

  onQualityChange = ({ value }: ValueChange<string>) => {
    const selectedIds = this.getSelectedIds();

    if (!selectedIds.length) {
      return;
    }

    const qualityId = Number(value);
    if (Number.isSafeInteger(qualityId) && this.props.qualities?.some((quality) => quality.id === qualityId)) {
      this.props.onQualityChange(selectedIds, qualityId);
    }
  };

  onReleaseGroupChange = ({ value }: ValueChange<string>) => {
    this.setState({ releaseGroup: value });
  };

  onSetReleaseGroupPress = () => {
    const ids = this.getSelectedIds();
    if (ids.length) {
      this.props.onReleaseGroupChange(ids, this.state.releaseGroup);
    }
  };

  //
  // Render

  render() {
    const {
      isDeleting,
      isSaving,
      saveError,
      isFetching,
      isPopulated,
      error,
      items,
      qualities,
      dispatchDeleteBookFile,
      ...otherProps
    } = this.props;

    const {
      allSelected,
      allUnselected,
      selectedState,
      releaseGroup,
      isConfirmDeleteModalOpen
    } = this.state;

    const qualityOptions: SelectOption[] = [
      { key: 'selectQuality', value: translate('SelectQuality'), isDisabled: true },
      ...[...qualities].reverse().map((quality) => ({ key: quality.id, value: quality.name }))
    ];

    const hasSelectedFiles = this.getSelectedIds().length > 0;

    return (
      <div>
        {
          isFetching && !isPopulated ?
            <LoadingIndicator /> :
            null
        }

        {
          !isFetching && error ?
            <Alert kind={kinds.DANGER}>{typeof error === 'string' ? error : getErrorMessage(error, 'Unable to load file properties.')}</Alert> :
            null
        }

        {
          saveError ?
            <Alert kind={kinds.DANGER}>
              {getErrorMessage(saveError, 'Unable to save file properties.')}
            </Alert> : null
        }

        {
          isPopulated && !items.length ?
            <div className={styles.blankpad}>
              No book files to manage.
            </div> :
            null
        }

        {
          isPopulated && items.length ?
            <div
              className={styles.filesTable}
            >
              <Table
                selectAll={true}
                allSelected={allSelected}
                allUnselected={allUnselected}
                onSelectAllChange={this.onSelectAllChange}
                {...otherProps}
              >
                <TableBody>
                  {
                    items.map((item) => {
                      return (
                        <BookFileEditorRow
                          key={item.id}
                          isSelected={selectedState[item.id] === true}
                          {...item}
                          onSelectedChange={this.onSelectedChange}
                          deleteBookFile={dispatchDeleteBookFile}
                        />
                      );
                    })
                  }
                </TableBody>
              </Table>
            </div> :
            null
        }

        {
          isPopulated && items.length ? (
            <div className={styles.actions}>
              <SpinnerButton
                kind={kinds.DANGER}
                isSpinning={isDeleting}
                isDisabled={!hasSelectedFiles}
                onPress={this.onDeletePress}
              >
                {translate('Delete')}
              </SpinnerButton>

              <div className={styles.selectInput}>
                <SelectInput
                  name="quality"
                  value="selectQuality"
                  values={qualityOptions}
                  isDisabled={!hasSelectedFiles || isSaving}
                  onChange={this.onQualityChange}
                />
              </div>
            </div>
          ) : null
        }

        {
          isPopulated && items.length ? (
            <div className={styles.releaseGroupEditor}>
              <label className={styles.releaseGroupInput}>
                {translate('ReleaseGroup')}
                <TextInput
                  name="releaseGroup"
                  value={releaseGroup}
                  readOnly={!hasSelectedFiles || isSaving}
                  onChange={this.onReleaseGroupChange}
                />
              </label>
              <SpinnerButton
                isSpinning={isSaving}
                isDisabled={!hasSelectedFiles}
                onPress={this.onSetReleaseGroupPress}
              >
                Set Release Group
              </SpinnerButton>
              <span>Leave blank to clear the group for selected files.</span>
            </div>
          ) : null
        }

        <ConfirmModal
          isOpen={isConfirmDeleteModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteSelectedBookFiles')}
          message={translate('DeleteSelectedBookFilesMessageText')}
          confirmLabel={translate('Delete')}
          onConfirm={this.onConfirmDelete}
          onCancel={this.onConfirmDeleteModalClose}
        />
      </div>
    );
  }
}

export default BookFileEditorTableContent;
