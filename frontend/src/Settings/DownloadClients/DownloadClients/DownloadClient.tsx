import { Component } from 'react';
import Card from 'Components/Card';
import Label from 'Components/Label';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TagList from 'Components/TagList';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import type { DownloadClientCard, TagSummary } from '../../providerCardState';
import EditDownloadClientModalConnector from './EditDownloadClientModalConnector';
import styles from './DownloadClient.css';

interface Props extends DownloadClientCard {
  onConfirmDeleteDownloadClient: (id: number) => void;
  tagList: TagSummary[];
}
interface State {
  isEditDownloadClientModalOpen: boolean;
  isDeleteDownloadClientModalOpen: boolean;
}

class DownloadClient extends Component<Props, State> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      isEditDownloadClientModalOpen: false,
      isDeleteDownloadClientModalOpen: false
    };
  }

  //
  // Listeners

  onEditDownloadClientPress = () => {
    this.setState({ isEditDownloadClientModalOpen: true });
  };

  onEditDownloadClientModalClose = () => {
    this.setState({ isEditDownloadClientModalOpen: false });
  };

  onDeleteDownloadClientPress = () => {
    this.setState({
      isEditDownloadClientModalOpen: false,
      isDeleteDownloadClientModalOpen: true
    });
  };

  onDeleteDownloadClientModalClose= () => {
    this.setState({ isDeleteDownloadClientModalOpen: false });
  };

  onConfirmDeleteDownloadClient = () => {
    this.props.onConfirmDeleteDownloadClient(this.props.id);
  };

  //
  // Render

  render() {
    const {
      id,
      name,
      enable,
      priority,
      tags,
      tagList
    } = this.props;

    return (
      <Card
        className={styles.downloadClient}
        overlayContent={true}
        onPress={this.onEditDownloadClientPress}
      >
        <div className={styles.name}>
          {name}
        </div>

        <div className={styles.enabled}>
          {
            enable ?
              <Label kind={kinds.SUCCESS}>
                Enabled
              </Label> :
              <Label
                kind={kinds.DISABLED}
                outline={true}
              >
                Disabled
              </Label>
          }

          {
            priority > 1 &&
              <Label
                kind={kinds.DISABLED}
                outline={true}
              >
                Priority: {priority}
              </Label>
          }
        </div>

        <TagList
          tags={tags}
          tagList={tagList}
        />

        <EditDownloadClientModalConnector
          id={id}
          isOpen={this.state.isEditDownloadClientModalOpen}
          onModalClose={this.onEditDownloadClientModalClose}
          onDeleteDownloadClientPress={this.onDeleteDownloadClientPress}
        />

        <ConfirmModal
          isOpen={this.state.isDeleteDownloadClientModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteDownloadClient')}
          message={translate('DeleteDownloadClientMessageText', { name })}
          confirmLabel={translate('Delete')}
          onConfirm={this.onConfirmDeleteDownloadClient}
          onCancel={this.onDeleteDownloadClientModalClose}
        />
      </Card>
    );
  }
}

export default DownloadClient;
