import { Component } from 'react';
import Card from 'Components/Card';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { icons, kinds } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import translate from 'Utilities/String/translate';
import type { FormatCard } from '../customFormatState';
import EditCustomFormatModalConnector from './EditCustomFormatModalConnector';
import ExportCustomFormatModal from './ExportCustomFormatModal';
import styles from './CustomFormat.css';

interface Props extends FormatCard {
  isDeleting: boolean;
  onConfirmDeleteCustomFormat: (id: number) => void;
  onCloneCustomFormatPress: (id: number) => void;
}
interface State {
  isEditCustomFormatModalOpen: boolean;
  isExportCustomFormatModalOpen: boolean;
  isDeleteCustomFormatModalOpen: boolean;
}

class CustomFormat extends Component<Props, State> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      isEditCustomFormatModalOpen: false,
      isExportCustomFormatModalOpen: false,
      isDeleteCustomFormatModalOpen: false
    };
  }

  //
  // Listeners

  onEditCustomFormatPress = () => {
    this.setState({ isEditCustomFormatModalOpen: true });
  };

  onEditCustomFormatModalClose = () => {
    this.setState({ isEditCustomFormatModalOpen: false });
  };

  onExportCustomFormatPress = () => {
    this.setState({ isExportCustomFormatModalOpen: true });
  };

  onExportCustomFormatModalClose = () => {
    this.setState({ isExportCustomFormatModalOpen: false });
  };

  onDeleteCustomFormatPress = () => {
    this.setState({
      isEditCustomFormatModalOpen: false,
      isDeleteCustomFormatModalOpen: true
    });
  };

  onDeleteCustomFormatModalClose = () => {
    this.setState({ isDeleteCustomFormatModalOpen: false });
  };

  onConfirmDeleteCustomFormat = () => {
    this.props.onConfirmDeleteCustomFormat(this.props.id);
  };

  onCloneCustomFormatPress = () => {
    const {
      id,
      onCloneCustomFormatPress
    } = this.props;

    onCloneCustomFormatPress(id);
  };

  //
  // Render

  render() {
    const {
      id,
      name,
      specifications,
      isDeleting
    } = this.props;

    return (
      <Card
        className={styles.customFormat}
        overlayContent={true}
        onPress={this.onEditCustomFormatPress}
      >
        <div className={styles.nameContainer}>
          <div className={styles.name}>
            {name}
          </div>

          <div className={styles.buttons}>
            <IconButton
              className={styles.cloneButton}
              title={translate('CloneCustomFormat')}
              name={icons.CLONE}
              onPress={this.onCloneCustomFormatPress}
            />

            <IconButton
              className={styles.cloneButton}
              title={translate('ExportCustomFormat')}
              name={icons.EXPORT}
              onPress={this.onExportCustomFormatPress}
            />
          </div>
        </div>

        <div>
          {
            specifications.map((item, index) => {
              if (!item) {
                return null;
              }

              let kind: Kind = kinds.DEFAULT;
              if (item.required) {
                kind = kinds.SUCCESS;
              }
              if (item.negate) {
                kind = kinds.DANGER;
              }

              return (
                <Label
                  className={styles.label}
                  key={index}
                  kind={kind}
                >
                  {item.name}
                </Label>
              );
            })
          }
        </div>

        <EditCustomFormatModalConnector
          id={id}
          isOpen={this.state.isEditCustomFormatModalOpen}
          onModalClose={this.onEditCustomFormatModalClose}
          onDeleteCustomFormatPress={this.onDeleteCustomFormatPress}
        />

        <ExportCustomFormatModal
          id={id}
          isOpen={this.state.isExportCustomFormatModalOpen}
          onModalClose={this.onExportCustomFormatModalClose}
        />

        <ConfirmModal
          isOpen={this.state.isDeleteCustomFormatModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteCustomFormat')}
          message={translate('DeleteCustomFormatMessageText', { name })}
          confirmLabel={translate('Delete')}
          isSpinning={isDeleting}
          onConfirm={this.onConfirmDeleteCustomFormat}
          onCancel={this.onDeleteCustomFormatModalClose}
        />
      </Card>
    );
  }
}

export default CustomFormat;
