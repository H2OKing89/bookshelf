import { Component } from 'react';
import Card from 'Components/Card';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import Tooltip from 'Components/Tooltip/Tooltip';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import EditQualityProfileModalConnector from './EditQualityProfileModalConnector';
import { isQualityLeaf, type QualityProfileSummary } from './qualityProfileSettingsState';
import styles from './QualityProfile.css';

interface Props extends QualityProfileSummary {
  isDeleting: boolean;
  onConfirmDeleteQualityProfile: (id: number) => void;
  onCloneQualityProfilePress: (id: number) => void;
}
interface State {
  isEditQualityProfileModalOpen: boolean;
  isDeleteQualityProfileModalOpen: boolean;
}
class QualityProfile extends Component<Props, State> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      isEditQualityProfileModalOpen: false,
      isDeleteQualityProfileModalOpen: false
    };
  }

  //
  // Listeners

  onEditQualityProfilePress = () => {
    this.setState({ isEditQualityProfileModalOpen: true });
  };

  onEditQualityProfileModalClose = () => {
    this.setState({ isEditQualityProfileModalOpen: false });
  };

  onDeleteQualityProfilePress = () => {
    this.setState({
      isEditQualityProfileModalOpen: false,
      isDeleteQualityProfileModalOpen: true
    });
  };

  onDeleteQualityProfileModalClose = () => {
    this.setState({ isDeleteQualityProfileModalOpen: false });
  };

  onConfirmDeleteQualityProfile = () => {
    this.props.onConfirmDeleteQualityProfile(this.props.id);
  };

  onCloneQualityProfilePress = () => {
    const {
      id,
      onCloneQualityProfilePress
    } = this.props;

    onCloneQualityProfilePress(id);
  };

  //
  // Render

  render() {
    const {
      id,
      name,
      upgradeAllowed,
      cutoff,
      items,
      isDeleting
    } = this.props;

    return (
      <Card
        className={styles.qualityProfile}
        overlayContent={true}
        onPress={this.onEditQualityProfilePress}
      >
        <div className={styles.nameContainer}>
          <div className={styles.name}>
            {name}
          </div>

          <IconButton
            className={styles.cloneButton}
            title={translate('CloneProfile')}
            name={icons.CLONE}
            onPress={this.onCloneQualityProfilePress}
          />
        </div>

        <div className={styles.qualities}>
          {
            items.map((item) => {
              if (!item.allowed) {
                return null;
              }

              if (isQualityLeaf(item)) {
                const isCutoff = upgradeAllowed && item.quality.id === cutoff;

                return (
                  <Label
                    key={item.quality.id}
                    kind={isCutoff ? kinds.INFO : kinds.DEFAULT}
                    title={isCutoff ? translate('IsCutoffUpgradeUntilThisQualityIsMetOrExceeded') : undefined}
                  >
                    {item.quality.name}
                  </Label>
                );
              }

              const isCutoff = upgradeAllowed && item.id === cutoff;

              return (
                <Tooltip
                  key={item.id}
                  className={styles.tooltipLabel}
                  anchor={
                    <Label
                      kind={isCutoff ? kinds.INFO : kinds.DEFAULT}
                      title={isCutoff ? translate('IsCutoffCutoff') : undefined}
                    >
                      {item.name}
                    </Label>
                  }
                  tooltip={
                    <div>
                      {
                        item.items.map((groupItem) => {
                          return (
                            <Label
                              key={groupItem.quality.id}
                              kind={isCutoff ? kinds.INFO : kinds.DEFAULT}
                              title={isCutoff ? translate('IsCutoffCutoff') : undefined}
                            >
                              {groupItem.quality.name}
                            </Label>
                          );
                        })
                      }
                    </div>
                  }
                  kind={kinds.INVERSE}
                  position={tooltipPositions.TOP}
                />
              );
            })
          }
        </div>

        <EditQualityProfileModalConnector
          id={id}
          isOpen={this.state.isEditQualityProfileModalOpen}
          onModalClose={this.onEditQualityProfileModalClose}
          onDeleteQualityProfilePress={this.onDeleteQualityProfilePress}
        />

        <ConfirmModal
          isOpen={this.state.isDeleteQualityProfileModalOpen}
          kind={kinds.DANGER}
          title={translate('DeleteQualityProfile')}
          message={translate('DeleteQualityProfileMessageText', { name })}
          confirmLabel={translate('Delete')}
          isSpinning={isDeleting}
          onConfirm={this.onConfirmDeleteQualityProfile}
          onCancel={this.onDeleteQualityProfileModalClose}
        />
      </Card>
    );
  }
}

export default QualityProfile;
