import { Component } from 'react';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import EditQualityProfileModalConnector from './EditQualityProfileModalConnector';
import QualityProfile from './QualityProfile';
import type { QualityProfileSummary } from './qualityProfileSettingsState';
import styles from './QualityProfiles.css';

interface Props {
  isFetching: boolean;
  isPopulated: boolean;
  error?: unknown;
  isDeleting: boolean;
  items: readonly QualityProfileSummary[];
  onConfirmDeleteQualityProfile: (id: number) => void;
  onCloneQualityProfilePress: (id: number) => void;
}

class QualityProfiles extends Component<Props, { isQualityProfileModalOpen: boolean }> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      isQualityProfileModalOpen: false
    };
  }

  //
  // Listeners

  onCloneQualityProfilePress = (id: number) => {
    this.props.onCloneQualityProfilePress(id);
    this.setState({ isQualityProfileModalOpen: true });
  };

  onEditQualityProfilePress = () => {
    this.setState({ isQualityProfileModalOpen: true });
  };

  onModalClose = () => {
    this.setState({ isQualityProfileModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      items,
      isDeleting,
      onConfirmDeleteQualityProfile,
      ...otherProps
    } = this.props;

    return (
      <FieldSet legend={translate('QualityProfiles')}>
        <PageSectionContent
          errorMessage={translate('UnableToLoadQualityProfiles')}
          {...otherProps}
        >
          <div className={styles.qualityProfiles}>
            {
              items.map((item) => {
                return (
                  <QualityProfile
                    key={item.id}
                    {...item}
                    isDeleting={isDeleting}
                    onConfirmDeleteQualityProfile={onConfirmDeleteQualityProfile}
                    onCloneQualityProfilePress={this.onCloneQualityProfilePress}
                  />
                );
              })
            }

            <Card
              className={styles.addQualityProfile}
              aria-label={translate('AddQualityProfile')}
              onPress={this.onEditQualityProfilePress}
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

          <EditQualityProfileModalConnector
            isOpen={this.state.isQualityProfileModalOpen}
            onModalClose={this.onModalClose}
          />
        </PageSectionContent>
      </FieldSet>
    );
  }
}

export default QualityProfiles;
