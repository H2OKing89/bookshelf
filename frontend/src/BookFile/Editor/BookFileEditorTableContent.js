import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import SelectInput from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { kinds } from 'Helpers/Props';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import getRemovedItems from 'Utilities/Object/getRemovedItems';
import hasDifferentItems from 'Utilities/Object/hasDifferentItems';
import translate from 'Utilities/String/translate';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import removeOldSelectedState from 'Utilities/Table/removeOldSelectedState';
import selectAll from 'Utilities/Table/selectAll';
import toggleSelected from 'Utilities/Table/toggleSelected';
import BookFileEditorRow from './BookFileEditorRow';
import styles from './BookFileEditorTableContent.css';

class BookFileEditorTableContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      allSelected: false,
      allUnselected: false,
      lastToggled: null,
      selectedState: {},
      releaseGroup: '',
      isConfirmDeleteModalOpen: false
    };
  }

  componentDidUpdate(prevProps) {
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
    const ids = getSelectedIds(this.state.selectedState);
    return ids;
  };

  getSelectedReleaseGroup = (selectedState) => {
    const selectedGroups = this.props.items
      .filter((item) => selectedState[item.id])
      .map((item) => item.releaseGroup || '');

    return selectedGroups.length && selectedGroups.every((group) => group === selectedGroups[0]) ?
      selectedGroups[0] : '';
  };

  //
  // Listeners

  onSelectAllChange = ({ value }) => {
    const selectedState = {};
    this.props.items.forEach((item) => {
      selectedState[item.id] = false;
    });
    const selection = selectAll(selectedState, value);
    this.setState({ ...selection, releaseGroup: this.getSelectedReleaseGroup(selection.selectedState) });
  };

  onSelectedChange = ({ id, value, shiftKey = false }) => {
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

  onQualityChange = ({ value }) => {
    const selectedIds = this.getSelectedIds();

    if (!selectedIds.length) {
      return;
    }

    this.props.onQualityChange(selectedIds, parseInt(value));
  };

  onReleaseGroupChange = ({ value }) => {
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

    const qualityOptions = _.reduceRight(qualities, (acc, quality) => {
      acc.push({
        key: quality.id,
        value: quality.name
      });

      return acc;
    }, [{ key: 'selectQuality', value: translate('SelectQuality'), isDisabled: true }]);

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
            <Alert kind={kinds.DANGER}>{error}</Alert> :
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
                          isSelected={selectedState[item.id]}
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

BookFileEditorTableContent.propTypes = {
  isDeleting: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  qualities: PropTypes.arrayOf(PropTypes.object).isRequired,
  onDeletePress: PropTypes.func.isRequired,
  onQualityChange: PropTypes.func.isRequired,
  onReleaseGroupChange: PropTypes.func.isRequired,
  dispatchDeleteBookFile: PropTypes.func.isRequired
};

export default BookFileEditorTableContent;
