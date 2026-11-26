import { Component } from 'react';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import type { IndexerCard, TagSummary } from '../../providerCardState';
import AddIndexerModal from './AddIndexerModal';
import EditIndexerModalConnector from './EditIndexerModalConnector';
import Indexer from './Indexer';
import styles from './Indexers.css';

interface Props {
  isFetching: boolean;
  isPopulated: boolean;
  error?: unknown;
  items: IndexerCard[];
  onConfirmDeleteIndexer: (id: number) => void;
  tagList: TagSummary[];
  dispatchCloneIndexer: (payload: { id: number }) => void;
}
interface State {
  isAddIndexerModalOpen: boolean;
  isEditIndexerModalOpen: boolean;
}

class Indexers extends Component<Props, State> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      isAddIndexerModalOpen: false,
      isEditIndexerModalOpen: false
    };
  }

  //
  // Listeners

  onAddIndexerPress = () => {
    this.setState({ isAddIndexerModalOpen: true });
  };

  onCloneIndexerPress = (id: number) => {
    this.props.dispatchCloneIndexer({ id });
    this.setState({ isEditIndexerModalOpen: true });
  };

  onAddIndexerModalClose = ({ indexerSelected = false }: { indexerSelected?: boolean } = {}) => {
    this.setState({
      isAddIndexerModalOpen: false,
      isEditIndexerModalOpen: indexerSelected
    });
  };

  onEditIndexerModalClose = () => {
    this.setState({ isEditIndexerModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      items,
      tagList,
      onConfirmDeleteIndexer,
      ...otherProps
    } = this.props;

    const {
      isAddIndexerModalOpen,
      isEditIndexerModalOpen
    } = this.state;

    const showPriority = items.some((index) => index.priority !== 25);

    return (
      <FieldSet legend={translate('Indexers')}>
        <PageSectionContent
          errorMessage={translate('UnableToLoadIndexers')}
          {...otherProps}
        >
          <div className={styles.indexers}>
            {
              items.map((item) => {
                return (
                  <Indexer
                    key={item.id}
                    {...item}
                    tagList={tagList}
                    showPriority={showPriority}
                    onCloneIndexerPress={this.onCloneIndexerPress}
                    onConfirmDeleteIndexer={onConfirmDeleteIndexer}
                  />
                );
              })
            }

            <Card
              className={styles.addIndexer}
              aria-label={translate('AddIndexer')}
              onPress={this.onAddIndexerPress}
            >
              <div className={styles.center}>
                <Icon
                  name={icons.ADD}
                  aria-hidden={true}
                  size={45}
                />
              </div>
            </Card>
          </div>

          <AddIndexerModal
            isOpen={isAddIndexerModalOpen}
            onModalClose={this.onAddIndexerModalClose}
          />

          <EditIndexerModalConnector
            isOpen={isEditIndexerModalOpen}
            onModalClose={this.onEditIndexerModalClose}
          />
        </PageSectionContent>
      </FieldSet>
    );
  }
}

export default Indexers;
