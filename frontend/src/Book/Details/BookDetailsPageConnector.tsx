import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { useParams } from 'react-router-dom';
import { createSelector } from 'reselect';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import translate from 'Utilities/String/translate';
import BookDetailsConnector from './BookDetailsConnector';

interface RouteProps { requestedSlug?: string }

function createMapStateToProps() {
  return createSelector(
    (_state: LegacyRootState, { requestedSlug }: RouteProps) => requestedSlug,
    (state: LegacyRootState) => state.books,
    (state: LegacyRootState) => state.authors,
    (titleSlug, books, author) => {
      const isFetching = books.isFetching === true || author.isFetching === true;
      const isPopulated = books.isPopulated === true && author.isPopulated === true;

      // if books have been fetched, make sure requested one exists
      // otherwise don't map titleSlug to trigger not found page
      if (!isFetching && isPopulated) {
        const bookIndex = Array.isArray(books.items) ? books.items.findIndex((item: unknown) => asStateRecord(item).titleSlug === titleSlug) : -1;
        if (bookIndex === -1) {
          return {
            titleSlug: undefined,
            isFetching,
            isPopulated
          };
        }
      }

      return {
        titleSlug,
        isFetching,
        isPopulated
      };
    }
  );
}

const connector = connect(createMapStateToProps);
type Props = RouteProps & ConnectedProps<typeof connector>;

class BookDetailsPage extends Component<Props, { hasMounted: boolean }> {

  constructor(props: Props) {
    super(props);
    this.state = { hasMounted: false };
  }
  //
  // Lifecycle

  componentDidMount() {
    this.populate();
  }

  //
  // Control

  populate = () => {
    this.setState({ hasMounted: true });
  };

  //
  // Render

  render() {
    const {
      titleSlug,
      isFetching,
      isPopulated
    } = this.props;

    if (!titleSlug) {
      return (
        <NotFound
          message={translate('SorryThatBookCannotBeFound')}
        />
      );
    }

    if ((isFetching || !this.state.hasMounted) ||
        (!isFetching && !isPopulated)) {
      return (
        <PageContent title={translate('Loading')}>
          <PageContentBody>
            <LoadingIndicator />
          </PageContentBody>
        </PageContent>
      );
    }

    if (!isFetching && isPopulated && this.state.hasMounted) {
      return (
        <BookDetailsConnector
          titleSlug={titleSlug}
        />
      );
    }

    return null;
  }
}

const ConnectedBookDetailsPage = connector(BookDetailsPage);

export default function BookDetailsPageConnector() {
  const { titleSlug } = useParams();

  return <ConnectedBookDetailsPage requestedSlug={titleSlug} />;
}
