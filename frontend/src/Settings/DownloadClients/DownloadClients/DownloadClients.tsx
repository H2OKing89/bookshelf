import { Component } from 'react';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import type { DownloadClientCard, TagSummary } from '../../providerCardState';
import AddDownloadClientModal from './AddDownloadClientModal';
import DownloadClient from './DownloadClient';
import EditDownloadClientModalConnector from './EditDownloadClientModalConnector';
import styles from './DownloadClients.css';

interface Props {
  isFetching: boolean;
  isPopulated: boolean;
  error?: unknown;
  items: DownloadClientCard[];
  onConfirmDeleteDownloadClient: (id: number) => void;
  tagList: TagSummary[];
}
interface State {
  isAddDownloadClientModalOpen: boolean;
  isEditDownloadClientModalOpen: boolean;
}

class DownloadClients extends Component<Props, State> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      isAddDownloadClientModalOpen: false,
      isEditDownloadClientModalOpen: false
    };
  }

  //
  // Listeners

  onAddDownloadClientPress = () => {
    this.setState({ isAddDownloadClientModalOpen: true });
  };

  onAddDownloadClientModalClose = ({ downloadClientSelected = false }: { downloadClientSelected?: boolean } = {}) => {
    this.setState({
      isAddDownloadClientModalOpen: false,
      isEditDownloadClientModalOpen: downloadClientSelected
    });
  };

  onEditDownloadClientModalClose = () => {
    this.setState({ isEditDownloadClientModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      items,
      onConfirmDeleteDownloadClient,
      tagList,
      ...otherProps
    } = this.props;

    const {
      isAddDownloadClientModalOpen,
      isEditDownloadClientModalOpen
    } = this.state;

    return (
      <FieldSet legend={translate('DownloadClients')}>
        <PageSectionContent
          errorMessage={translate('UnableToLoadDownloadClients')}
          {...otherProps}
        >
          <div className={styles.downloadClients}>
            {
              items.map((item) => {
                return (
                  <DownloadClient
                    key={item.id}
                    {...item}
                    tagList={tagList}
                    onConfirmDeleteDownloadClient={onConfirmDeleteDownloadClient}
                  />
                );
              })
            }

            <Card
              className={styles.addDownloadClient}
              aria-label={translate('AddDownloadClient')}
              onPress={this.onAddDownloadClientPress}
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

          <AddDownloadClientModal
            isOpen={isAddDownloadClientModalOpen}
            onModalClose={this.onAddDownloadClientModalClose}
          />

          <EditDownloadClientModalConnector
            isOpen={isEditDownloadClientModalOpen}
            onModalClose={this.onEditDownloadClientModalClose}
          />
        </PageSectionContent>
      </FieldSet>
    );
  }
}

export default DownloadClients;
