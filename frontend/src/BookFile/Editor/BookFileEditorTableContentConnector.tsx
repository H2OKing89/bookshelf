import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { AuthorId, BookFileId, BookId } from 'Book/BookIdentity';
import type { QualityModel } from 'Book/LibraryModels';
import { deleteBookFile, deleteBookFiles, setBookFilesSort, updateBookFiles } from 'Store/Actions/bookFileActions';
import { fetchQualityProfileSchema } from 'Store/Actions/settingsActions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import { asStateRecord, type LegacyDispatch, type LegacyRootState } from 'Store/StateTypes';
import { directionValue, editorColumns, editorFiles, qualityChoices } from './bookFileEditorState';
import BookFileEditorTableContent from './BookFileEditorTableContent';

export interface FileEditorScope {
  authorId: AuthorId;
  bookId?: BookId;
}

function createMapStateToProps() {
  return createSelector(
    createClientSideCollectionSelector('bookFiles'),
    (state: LegacyRootState) => state.settings.qualityProfiles,
    (bookFiles, qualityProfiles) => {
      const schema = asStateRecord(qualityProfiles);
      const choices = qualityChoices(asStateRecord(schema.schema).items);
      return {
        qualities: choices,
        items: editorFiles(bookFiles.items),
        columns: editorColumns(bookFiles.columns),
        sortKey: typeof bookFiles.sortKey === 'string' ? bookFiles.sortKey : undefined,
        sortDirection: directionValue(bookFiles.sortDirection),
        isFetching: bookFiles.isFetching === true || schema.isSchemaFetching === true,
        isPopulated: bookFiles.isPopulated === true,
        error: bookFiles.error ?? (schema.schemaError ? 'Unable to load qualities' : null),
        isDeleting: bookFiles.isDeleting === true,
        isSaving: bookFiles.isSaving === true,
        saveError: bookFiles.saveError
      };
    }
  );
}

function createMapDispatchToProps(dispatch: LegacyDispatch) {
  return {
    onSortPress(sortKey: string) {
      dispatch(setBookFilesSort({ sortKey }));
    },
    dispatchFetchQualityProfileSchema() {
      dispatch(fetchQualityProfileSchema());
    },
    dispatchUpdateBookFiles(updateProps: { bookFileIds: BookFileId[]; quality?: QualityModel; releaseGroup?: string }) {
      dispatch(updateBookFiles(updateProps));
    },
    onDeletePress(bookFileIds: BookFileId[]) {
      dispatch(deleteBookFiles({ bookFileIds }));
    },
    dispatchDeleteBookFile(options: { id: BookFileId }) {
      dispatch(deleteBookFile(options));
    }
  };
}

const connector = connect(createMapStateToProps, createMapDispatchToProps);
type Props = FileEditorScope & ConnectedProps<typeof connector>;

class BookFileEditorTableContentConnector extends Component<Props> {
  componentDidMount() {
    this.props.dispatchFetchQualityProfileSchema();
  }

  onQualityChange = (bookFileIds: BookFileId[], qualityId: number) => {
    const quality = this.props.qualities.find((item) => item.id === qualityId);
    if (quality) {
      this.props.dispatchUpdateBookFiles({ bookFileIds, quality: { quality, revision: { version: 1, real: 0 } } });
    }
  };

  onReleaseGroupChange = (bookFileIds: BookFileId[], releaseGroup: string) => {
    this.props.dispatchUpdateBookFiles({ bookFileIds, releaseGroup });
  };

  render() {
    return <BookFileEditorTableContent {...this.props} onQualityChange={this.onQualityChange} onReleaseGroupChange={this.onReleaseGroupChange} />;
  }
}

export default connector(BookFileEditorTableContentConnector);
