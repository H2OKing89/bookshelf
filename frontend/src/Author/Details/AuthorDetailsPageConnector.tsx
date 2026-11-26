import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { type NavigateFunction, useNavigate, useParams } from 'react-router-dom';
import { createSelector } from 'reselect';
import { type AuthorId, parseResourceId } from 'Book/BookIdentity';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import AuthorDetailsConnector from './AuthorDetailsConnector';
import AuthorReconciliationModal from './AuthorReconciliationModal';
import styles from './AuthorDetails.css';

interface RouteProps { requestedSlug?: string; navigate: NavigateFunction }

function createMapStateToProps() {
  return createSelector(
    (_state: LegacyRootState, { requestedSlug }: RouteProps) => requestedSlug,
    (state: LegacyRootState) => state.authors,
    (titleSlug, authors) => {
      const {
        isFetching,
        isPopulated,
        error,
        items
      } = authors;

      const authorIndex = Array.isArray(items) ? items.findIndex((item: unknown) => asStateRecord(item).titleSlug === titleSlug) : -1;

      if (authorIndex > -1 && Array.isArray(items)) {
        const author = asStateRecord(items[authorIndex]);
        return {
          isFetching: isFetching === true,
          isPopulated: isPopulated === true,
          error,
          titleSlug,
          nameOnlyAuthorId: typeof author.foreignAuthorId === 'string' && /^audible:[a-z]{2}:name:.+/.test(author.foreignAuthorId) ? parseResourceId(author.id, 'author') : null
        };
      }

      return {
        titleSlug: undefined,
        nameOnlyAuthorId: null,
        isFetching: isFetching === true,
        isPopulated: isPopulated === true,
        error
      };
    }
  );
}

const connector = connect(createMapStateToProps);
type Props = RouteProps & ConnectedProps<typeof connector> & { navigate: NavigateFunction };
interface State { reconciliationAuthorId: AuthorId | null }

class AuthorDetailsPage extends Component<Props, State> {

  state: State = {
    reconciliationAuthorId: null
  };

  //
  // Lifecycle

  componentDidUpdate() {
    if (!this.props.titleSlug && !this.state.reconciliationAuthorId) {
      this.props.navigate('/');
      return;
    }
  }

  onReconcileAuthorPress = () => {
    this.setState({ reconciliationAuthorId: this.props.nameOnlyAuthorId });
  };

  onAuthorReconciliationClose = () => {
    this.setState({ reconciliationAuthorId: null });
  };

  onAuthorReconciled = (titleSlug: string) => {
    window.location.assign(`${window.Readarr.urlBase}/author/${encodeURIComponent(titleSlug)}`);
  };

  //
  // Render

  render() {
    const {
      titleSlug,
      isFetching,
      isPopulated,
      error,
      nameOnlyAuthorId
    } = this.props;
    const { reconciliationAuthorId } = this.state;

    if (isFetching && !isPopulated) {
      return (
        <PageContent title={translate('Loading')}>
          <PageContentBody>
            <LoadingIndicator />
          </PageContentBody>
        </PageContent>
      );
    }

    if (!isFetching && !!error) {
      return (
        <div className={styles.errorMessage}>
          {getErrorMessage(error, 'Failed to load author from API')}
        </div>
      );
    }

    if (!titleSlug && !reconciliationAuthorId) {
      return (
        <NotFound
          message={translate('SorryThatAuthorCannotBeFound')}
        />
      );
    }

    return (
      <>
        {
          titleSlug &&
            <AuthorDetailsConnector
              titleSlug={titleSlug}
              onReconcileAuthorPress={nameOnlyAuthorId ? this.onReconcileAuthorPress : undefined}
            />
        }
        {
          reconciliationAuthorId &&
            <AuthorReconciliationModal
              sourceAuthorId={reconciliationAuthorId}
              onModalClose={this.onAuthorReconciliationClose}
              onReconciled={this.onAuthorReconciled}
            />
        }
      </>
    );
  }
}

const ConnectedAuthorDetailsPage = connector(AuthorDetailsPage);

export default function AuthorDetailsPageConnector() {
  const { titleSlug } = useParams();
  const navigate = useNavigate();

  return <ConnectedAuthorDetailsPage requestedSlug={titleSlug} navigate={navigate} />;
}
