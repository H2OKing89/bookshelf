import React, { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { clearPaths, fetchPaths } from 'Store/Actions/pathActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import PathInput, { type PathInputProps, type PathSuggestion } from './PathInput';

type OwnProps = Omit<PathInputProps, 'paths' | 'onFetchPaths' | 'onClearPaths' | 'includeFiles'> & { includeFiles?: boolean };

export function filterPaths(paths: unknown): PathSuggestion[] {
  const section = asStateRecord(paths);
  const currentPath = typeof section.currentPath === 'string' ? section.currentPath : '';
  const directories = Array.isArray(section.directories) ? section.directories : [];
  const files = Array.isArray(section.files) ? section.files : [];

  return [...directories, ...files].flatMap((candidate: unknown) => {
    const item = asStateRecord(candidate);

    if (typeof item.path !== 'string' || !item.path.toLowerCase().startsWith(currentPath.toLowerCase())) {
      return [];
    }

    return [{ path: item.path, type: typeof item.type === 'string' ? item.type : undefined }];
  });
}

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.paths,
    (paths) => {
      return { paths: filterPaths(paths) };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchPaths: fetchPaths,
  dispatchClearPaths: clearPaths
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class PathInputConnector extends Component<Props> {
  static defaultProps = { includeFiles: false };

  //
  // Listeners

  onFetchPaths = (path: string) => {
    const {
      includeFiles,
      dispatchFetchPaths
    } = this.props;

    dispatchFetchPaths({
      path,
      includeFiles
    });
  };

  onClearPaths = () => {
    this.props.dispatchClearPaths();
  };

  //
  // Render

  render() {
    return (
      <PathInput
        onFetchPaths={this.onFetchPaths}
        onClearPaths={this.onClearPaths}
        {...this.props}
        includeFiles={this.props.includeFiles ?? false}
      />
    );
  }
}

export default connector(PathInputConnector);
