import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type Author from 'Author/Author';
import type { AuthorId } from 'Book/BookIdentity';
import { authorHeaderData } from 'Book/Details/detailPresentation';
import { toggleAuthorMonitored } from 'Store/Actions/authorActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import type { ResourceCollection } from 'Store/Selectors/selectById';
import AuthorDetailsHeader from './AuthorDetailsHeader';

interface OwnProps {
  authorId: AuthorId;
  width: number;
}

function createMapStateToProps() {
  return createSelector(
    (state: { authors: ResourceCollection<Author> & { isSaving?: boolean } }) => state.authors.isSaving === true,
    createAuthorSelector(),
    createDimensionsSelector(),
    (isSaving, author, dimensions) => ({
      ...authorHeaderData(author),
      isSaving,
      isSmallScreen: dimensions.isSmallScreen
    })
  );
}

const connector = connect(createMapStateToProps, { toggleAuthorMonitored });
type Props = OwnProps & ConnectedProps<typeof connector>;

class AuthorDetailsHeaderConnector extends Component<Props> {
  onMonitorTogglePress = (monitored: boolean) => {
    this.props.toggleAuthorMonitored({ authorId: this.props.authorId, monitored });
  };

  render() {
    return <AuthorDetailsHeader {...this.props} id={this.props.authorId} onMonitorTogglePress={this.onMonitorTogglePress} />;
  }
}

export default connector(AuthorDetailsHeaderConnector);
