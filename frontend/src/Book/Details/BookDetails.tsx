import { Component } from 'react';
import { Tab, TabList, TabPanel, Tabs } from 'react-tabs';
import AuthorHistoryTable from 'Author/History/AuthorHistoryTable';
import type { AuthorId, BookId } from 'Book/BookIdentity';
import DeleteBookModal from 'Book/Delete/DeleteBookModal';
import EditBookModalConnector from 'Book/Edit/EditBookModalConnector';
import LocalRecordingModal from 'Book/Edit/LocalRecordingModal';
import type { LibraryStatistics } from 'Book/LibraryPresentation';
import RecordingWorkCoverage from 'Book/WorkCoverage/RecordingWorkCoverage';
import BookFileEditorTable from 'BookFile/Editor/BookFileEditorTable';
import IconButton from 'Components/Link/IconButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import SwipeHeaderConnector from 'Components/Swipe/SwipeHeaderConnector';
import { icons } from 'Helpers/Props';
import InteractiveSearchTable from 'InteractiveSearch/InteractiveSearchTable';
import OrganizePreviewModalConnector from 'Organize/OrganizePreviewModalConnector';
import RetagPreviewModalConnector from 'Retag/RetagPreviewModalConnector';
import translate from 'Utilities/String/translate';
import BookDetailsHeaderConnector from './BookDetailsHeaderConnector';
import BookRecordingMetadataConnector from './BookRecordingMetadataConnector';
import styles from './BookDetails.css';

export interface BookNavigationEntry { id: BookId; title: string; titleSlug: string }

export interface BookDetailsProps {
  id: BookId;
  title: string;
  foreignBookId?: string | null;
  isRefreshing: boolean;
  isRefreshingMediaInfo: boolean;
  isFetching: boolean;
  isPopulated: boolean;
  bookFilesError?: unknown;
  hasBookFiles: boolean;
  author: { id: AuthorId; authorName: string; titleSlug: string };
  previousBook: BookNavigationEntry;
  nextBook: BookNavigationEntry;
  isSearching: boolean;
  onRefreshPress: () => void;
  onRefreshMediaInfoPress: () => void;
  onSearchPress: () => void;
  statistics?: LibraryStatistics;
  onLocalMetadataSaved: (book: unknown) => void;
}

interface State {
  isOrganizeModalOpen: boolean;
  isRetagModalOpen: boolean;
  isEditBookModalOpen: boolean;
  isLocalMetadataModalOpen: boolean;
  isDeleteBookModalOpen: boolean;
}

class BookDetails extends Component<BookDetailsProps, State> {

  //
  // Lifecycle

  constructor(props: BookDetailsProps) {
    super(props);

    this.state = {
      isOrganizeModalOpen: false,
      isRetagModalOpen: false,
      isEditBookModalOpen: false,
      isLocalMetadataModalOpen: false,
      isDeleteBookModalOpen: false
    };
  }

  componentDidUpdate(previousProps: BookDetailsProps) {
    if (previousProps.id !== this.props.id && this.state.isLocalMetadataModalOpen) {
      this.onLocalMetadataClose();
    }
  }

  //
  // Listeners

  onOrganizePress = () => {
    this.setState({ isOrganizeModalOpen: true });
  };

  onOrganizeModalClose = () => {
    this.setState({ isOrganizeModalOpen: false });
  };

  onRetagPress = () => {
    this.setState({ isRetagModalOpen: true });
  };

  onRetagModalClose = () => {
    this.setState({ isRetagModalOpen: false });
  };

  onEditBookPress = () => {
    this.setState({ isEditBookModalOpen: true });
  };

  onEditBookModalClose = () => {
    this.setState({ isEditBookModalOpen: false });
  };

  onLocalMetadataPress = () => {
    this.setState({ isLocalMetadataModalOpen: true });
  };

  onLocalMetadataClose = () => {
    this.setState({ isLocalMetadataModalOpen: false });
  };

  onLocalMetadataSaved = (book: unknown) => {
    this.onLocalMetadataClose();
    this.props.onLocalMetadataSaved(book);
  };

  onDeleteBookPress = () => {
    this.setState({
      isEditBookModalOpen: false,
      isDeleteBookModalOpen: true
    });
  };

  onDeleteBookModalClose = () => {
    this.setState({ isDeleteBookModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      id,
      title,
      foreignBookId,
      isRefreshing,
      isRefreshingMediaInfo,
      isFetching,
      isPopulated,
      bookFilesError,
      hasBookFiles,
      author,
      previousBook,
      nextBook,
      isSearching,
      onRefreshPress,
      onRefreshMediaInfoPress,
      onSearchPress,
      statistics = {}
    } = this.props;

    const {
      bookFileCount = 0
    } = statistics;

    const {
      isOrganizeModalOpen,
      isRetagModalOpen,
      isEditBookModalOpen,
      isLocalMetadataModalOpen,
      isDeleteBookModalOpen
    } = this.state;

    return (
      <PageContent title={title}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('Refresh')}
              iconName={icons.REFRESH}
              spinningName={icons.REFRESH}
              title={translate('RefreshInformation')}
              isSpinning={isRefreshing}
              onPress={onRefreshPress}
            />

            <PageToolbarButton
              label={translate('SearchBook')}
              iconName={icons.SEARCH}
              isSpinning={isSearching}
              onPress={onSearchPress}
            />

            <PageToolbarSeparator />

            <PageToolbarButton
              label="Refresh media info"
              title="Read audio details from the files"
              iconName={icons.MEDIA_INFO}
              spinningName={icons.REFRESH}
              isDisabled={!hasBookFiles}
              isSpinning={isRefreshingMediaInfo}
              onPress={onRefreshMediaInfoPress}
            />

            <PageToolbarButton
              label={translate('PreviewRename')}
              iconName={icons.ORGANIZE}
              isDisabled={!hasBookFiles}
              onPress={this.onOrganizePress}
            />

            <PageToolbarButton
              label={translate('PreviewRetag')}
              iconName={icons.RETAG}
              isDisabled={!hasBookFiles}
              onPress={this.onRetagPress}
            />

            <PageToolbarSeparator />

            <PageToolbarButton
              label={translate('Edit')}
              iconName={icons.EDIT}
              onPress={this.onEditBookPress}
            />

            {foreignBookId?.startsWith('local:recording:') &&
              <PageToolbarButton
                label="Edit local metadata"
                iconName={icons.EDIT}
                onPress={this.onLocalMetadataPress}
              />
            }

            <PageToolbarButton
              label={translate('Delete')}
              iconName={icons.DELETE}
              onPress={this.onDeleteBookPress}
            />

          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody innerClassName={styles.innerContentBody}>
          <SwipeHeaderConnector
            nextLink={`/book/${nextBook.titleSlug}`}
            nextComponent={(width: number) => (
              <BookDetailsHeaderConnector
                bookId={nextBook.id}
                author={author}
                width={width}
              />
            )}
            prevLink={`/book/${previousBook.titleSlug}`}
            prevComponent={(width: number) => (
              <BookDetailsHeaderConnector
                bookId={previousBook.id}
                author={author}
                width={width}
              />
            )}
            currentComponent={(width: number) => (
              <BookDetailsHeaderConnector
                bookId={id}
                author={author}
                width={width}
              />
            )}
          >
            <div className={styles.bookNavigationButtons}>
              <IconButton
                className={styles.bookNavigationButton}
                name={icons.ARROW_LEFT}
                size={30}
                title={translate('GoToInterp', { 0: previousBook.title })}
                to={`/book/${previousBook.titleSlug}`}
              />

              <IconButton
                className={styles.bookUpButton}
                name={icons.ARROW_UP}
                size={30}
                title={translate('GoToInterp', { 0: author.authorName })}
                to={`/author/${author.titleSlug}`}
              />

              <IconButton
                className={styles.bookNavigationButton}
                name={icons.ARROW_RIGHT}
                size={30}
                title={translate('GoToInterp', { 0: nextBook.title })}
                to={`/book/${nextBook.titleSlug}`}
              />
            </div>
          </SwipeHeaderConnector>

          <div className={styles.contentContainer}>
            <BookRecordingMetadataConnector bookId={id} />
            {
              !isPopulated && !bookFilesError &&
                <LoadingIndicator />
            }

            {
              !isFetching && !!bookFilesError &&
                <div>
                  {translate('LoadingBookFilesFailed')}
                </div>
            }

            <Tabs>
              <TabList
                className={styles.tabList}
              >
                <Tab
                  className={styles.tab}
                  selectedClassName={styles.selectedTab}
                >
                  {translate('History')}
                </Tab>

                <Tab
                  className={styles.tab}
                  selectedClassName={styles.selectedTab}
                >
                  {translate('Search')}
                </Tab>

                <Tab
                  className={styles.tab}
                  selectedClassName={styles.selectedTab}
                >
                  {translate('FilesTotal', { 0: bookFileCount })}
                </Tab>

                <Tab className={styles.tab} selectedClassName={styles.selectedTab}>
                  Work coverage
                </Tab>

              </TabList>

              <TabPanel>
                <AuthorHistoryTable
                  authorId={author.id}
                  bookId={id}
                />
              </TabPanel>

              <TabPanel>
                <InteractiveSearchTable
                  bookId={id}
                  type="book"
                />
              </TabPanel>

              <TabPanel>
                <BookFileEditorTable
                  authorId={author.id}
                  bookId={id}
                />
              </TabPanel>

              <TabPanel>
                <RecordingWorkCoverage key={id} bookId={id} />
              </TabPanel>
            </Tabs>
          </div>

          <OrganizePreviewModalConnector
            isOpen={isOrganizeModalOpen}
            authorId={author.id}
            bookId={id}
            onModalClose={this.onOrganizeModalClose}
          />

          <RetagPreviewModalConnector
            isOpen={isRetagModalOpen}
            authorId={author.id}
            bookId={id}
            onModalClose={this.onRetagModalClose}
          />

          <EditBookModalConnector
            isOpen={isEditBookModalOpen}
            bookId={id}
            authorId={author.id}
            onModalClose={this.onEditBookModalClose}
            onDeleteAuthorPress={this.onDeleteBookPress}
          />

          {foreignBookId?.startsWith('local:recording:') &&
            <LocalRecordingModal
              key={id}
              isOpen={isLocalMetadataModalOpen}
              bookId={id}
              onModalClose={this.onLocalMetadataClose}
              onCreated={this.onLocalMetadataSaved}
            />
          }

          <DeleteBookModal
            isOpen={isDeleteBookModalOpen}
            bookId={id}
            authorSlug={author.titleSlug}
            onModalClose={this.onDeleteBookModalClose}
          />

        </PageContentBody>
      </PageContent>
    );
  }
}

export default BookDetails;
