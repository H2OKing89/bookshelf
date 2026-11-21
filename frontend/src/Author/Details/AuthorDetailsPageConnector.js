import { push } from 'connected-react-router';
import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import AuthorDetailsConnector from './AuthorDetailsConnector';
import AuthorReconciliationModal from './AuthorReconciliationModal';
import styles from './AuthorDetails.css';

function createMapStateToProps() {
  return createSelector(
    (state, { match }) => match,
    (state) => state.authors,
    (match, authors) => {
      const titleSlug = match.params.titleSlug;
      const {
        isFetching,
        isPopulated,
        error,
        items
      } = authors;

      const authorIndex = _.findIndex(items, { titleSlug });

      if (authorIndex > -1) {
        const author = items[authorIndex];
        return {
          isFetching,
          isPopulated,
          titleSlug,
          nameOnlyAuthorId: /^audible:[a-z]{2}:name:.+/.test(author.foreignAuthorId) ? author.id : null
        };
      }

      return {
        isFetching,
        isPopulated,
        error
      };
    }
  );
}

const mapDispatchToProps = {
  push
};

class AuthorDetailsPageConnector extends Component {

  state = {
    reconciliationAuthorId: null
  };

  //
  // Lifecycle

  componentDidUpdate(prevProps) {
    if (!this.props.titleSlug && !this.state.reconciliationAuthorId) {
      this.props.push(`${window.Readarr.urlBase}/`);
      return;
    }
  }

  onReconcileAuthorPress = () => {
    this.setState({ reconciliationAuthorId: this.props.nameOnlyAuthorId });
  };

  onAuthorReconciliationClose = () => {
    this.setState({ reconciliationAuthorId: null });
  };

  onAuthorReconciled = (titleSlug) => {
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

AuthorDetailsPageConnector.propTypes = {
  titleSlug: PropTypes.string,
  nameOnlyAuthorId: PropTypes.number,
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  match: PropTypes.shape({ params: PropTypes.shape({ titleSlug: PropTypes.string.isRequired }).isRequired }).isRequired,
  push: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AuthorDetailsPageConnector);
