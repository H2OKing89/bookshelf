import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { BookFileId } from 'Book/BookIdentity';
import type { LibraryBookFile } from 'Book/LibraryModels';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { fetchBookFiles } from 'Store/Actions/bookFileActions';
import type { ResourceCollection } from 'Store/Selectors/selectById';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import FileDetails from './FileDetails';

function createMapStateToProps() {
  return createSelector(
    (state: { bookFiles: ResourceCollection<LibraryBookFile> & { isFetching?: boolean; error?: unknown } }) => state.bookFiles,
    (bookFiles) => {
      return {
        items: bookFiles.items,
        isFetching: bookFiles.isFetching === true,
        error: bookFiles.error
      };
    }
  );
}

const mapDispatchToProps = {
  fetchBookFiles
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = { id: BookFileId } & ConnectedProps<typeof connector>;

class FileDetailsConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount() {
    this.props.fetchBookFiles({ id: this.props.id });
  }

  //
  // Render

  render() {
    const {
      items,
      id,
      isFetching,
      error
    } = this.props;

    const item = items.find((file) => file.id === id);
    const errorMessage = getErrorMessage(error, 'Unable to load file details');

    if (isFetching) {
      return (
        <LoadingIndicator />
      );
    } else if (error) {
      return (
        <div>{errorMessage}</div>
      );
    }

    if (!item) {
      return <div>This file is no longer available.</div>;
    }

    if (!item.audioTags && !item.releaseGroupEvidence) {
      return <div>No audio tags are available for this file.</div>;
    }

    return (
      <FileDetails
        audioTags={item.audioTags ?? { trackNumbers: [] }}
        filename={item.path}
        releaseGroup={item.releaseGroup}
        releaseGroupEvidence={item.releaseGroupEvidence}
      />
    );

  }
}

export default connector(FileDetailsConnector);
