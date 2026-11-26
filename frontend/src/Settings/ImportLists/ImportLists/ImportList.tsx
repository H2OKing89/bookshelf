import { Component } from 'react';
import Card from 'Components/Card';
import Label from 'Components/Label';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import formatShortTimeSpan from 'Utilities/Date/formatShortTimeSpan';
import translate from 'Utilities/String/translate';
import type { ImportListCard } from '../../providerCardState';
import EditImportListModalConnector from './EditImportListModalConnector';
import styles from './ImportList.css';

interface Props extends ImportListCard {
  onConfirmDeleteImportList: (id: number) => void;
}
interface State {
  isEditImportListModalOpen: boolean;
  isDeleteImportListModalOpen: boolean;
}

class ImportList extends Component<Props, State> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      isEditImportListModalOpen: false,
      isDeleteImportListModalOpen: false
    };
  }

  //
  // Listeners

  onEditImportListPress = () => {
    this.setState({ isEditImportListModalOpen: true });
  };

  onEditImportListModalClose = () => {
    this.setState({ isEditImportListModalOpen: false });
  };

  onDeleteImportListPress = () => {
    this.setState({
      isEditImportListModalOpen: false,
      isDeleteImportListModalOpen: true
    });
  };

  onDeleteImportListModalClose= () => {
    this.setState({ isDeleteImportListModalOpen: false });
  };

  onConfirmDeleteImportList = () => {
    this.props.onConfirmDeleteImportList(this.props.id);
  };

  //
  // Render

  render() {
    const {
      id,
      name,
      enableAutomaticAdd,
      minRefreshInterval,
      shouldSearch
    } = this.props;

    return (
      <Card
        className={styles.list}
        overlayContent={true}
        onPress={this.onEditImportListPress}
      >
        <div className={styles.name}>
          {name}
        </div>

        <div className={styles.enabled}>
          {
            enableAutomaticAdd &&
              <Label kind={kinds.SUCCESS}>
                Automatic Add
              </Label>
          }

          {
            shouldSearch &&
              <Label kind={kinds.SUCCESS}>
                Automatic Search
              </Label>
          }
        </div>

        <div className={styles.enabled}>
          <Label
            kind={kinds.INFO}
            title={translate('ListRefreshInterval')}
          >
            {`${translate('Refresh')}: ${formatShortTimeSpan(minRefreshInterval)}`}
          </Label>
        </div>

        <EditImportListModalConnector
          id={id}
          isOpen={this.state.isEditImportListModalOpen}
          onModalClose={this.onEditImportListModalClose}
          onDeleteImportListPress={this.onDeleteImportListPress}
        />

        <ConfirmModal
          isOpen={this.state.isDeleteImportListModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteImportList')}
          message={translate('DeleteImportListMessageText', { name })}
          confirmLabel={translate('Delete')}
          onConfirm={this.onConfirmDeleteImportList}
          onCancel={this.onDeleteImportListModalClose}
        />
      </Card>
    );
  }
}

export default ImportList;
