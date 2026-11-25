import classNames from 'classnames';
import React, { Component, type RefCallback } from 'react';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import TagList from 'Components/TagList';
import { icons, kinds } from 'Helpers/Props';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import EditDelayProfileModalConnector from './EditDelayProfileModalConnector';
import styles from './DelayProfile.css';

function getDelay(enabled: boolean, delay: number) {
  if (!enabled) {
    return '-';
  }

  if (!delay) {
    return 'No Delay';
  }

  if (delay === 1) {
    return '1 Minute';
  }

  // TODO: use better units of time than just minutes
  return `${delay} Minutes`;
}

export interface DelayProfileDragItem {
  id: number;
  order: number;
  enableUsenet: boolean;
  enableTorrent: boolean;
  preferredProtocol: string;
  usenetDelay: number;
  torrentDelay: number;
  tags: number[];
  tagList: { id: number; label: string }[];
}

interface DelayProfileProps extends DelayProfileDragItem {
  isDragging: boolean;
  isPreview?: boolean;
  dragHandleRef?: RefCallback<HTMLDivElement>;
  onConfirmDeleteDelayProfile?: (id: number) => void;
}

interface DelayProfileState {
  isEditDelayProfileModalOpen: boolean;
  isDeleteDelayProfileModalOpen: boolean;
}

class DelayProfile extends Component<DelayProfileProps, DelayProfileState> {

  //
  // Lifecycle

  constructor(props: DelayProfileProps) {
    super(props);

    this.state = {
      isEditDelayProfileModalOpen: false,
      isDeleteDelayProfileModalOpen: false
    };
  }

  //
  // Listeners

  onEditDelayProfilePress = () => {
    this.setState({ isEditDelayProfileModalOpen: true });
  };

  onEditDelayProfileModalClose = () => {
    this.setState({ isEditDelayProfileModalOpen: false });
  };

  onDeleteDelayProfilePress = () => {
    this.setState({
      isEditDelayProfileModalOpen: false,
      isDeleteDelayProfileModalOpen: true
    });
  };

  onDeleteDelayProfileModalClose = () => {
    this.setState({ isDeleteDelayProfileModalOpen: false });
  };

  onConfirmDeleteDelayProfile = () => {
    this.props.onConfirmDeleteDelayProfile?.(this.props.id);
  };

  //
  // Render

  render() {
    const {
      id,
      enableUsenet,
      enableTorrent,
      preferredProtocol,
      usenetDelay,
      torrentDelay,
      tags,
      tagList,
      isDragging,
      dragHandleRef,
      isPreview
    } = this.props;

    let preferred = titleCase(preferredProtocol);

    if (!enableUsenet) {
      preferred = 'Only Torrent';
    } else if (!enableTorrent) {
      preferred = 'Only Usenet';
    }

    return (
      <div
        className={classNames(
          styles.delayProfile,
          isDragging && styles.isDragging
        )}
      >
        <div className={styles.column}>{preferred}</div>
        <div className={styles.column}>{getDelay(enableUsenet, usenetDelay)}</div>
        <div className={styles.column}>{getDelay(enableTorrent, torrentDelay)}</div>

        <TagList
          tags={tags}
          tagList={tagList}
        />

        <div className={styles.actions}>
          <Link
            className={id === 1 ? styles.editButton : undefined}
            onPress={isPreview ? undefined : this.onEditDelayProfilePress}
            tabIndex={isPreview ? -1 : undefined}
          >
            <Icon name={icons.EDIT} />
          </Link>

          {
            id !== 1 &&
              <div ref={dragHandleRef} className={styles.dragHandle}>
                <Icon
                  className={styles.dragIcon}
                  name={icons.REORDER}
                />
              </div>
          }
        </div>

        {
          !isPreview &&
            <>
              <EditDelayProfileModalConnector
                id={id}
                isOpen={this.state.isEditDelayProfileModalOpen}
                onModalClose={this.onEditDelayProfileModalClose}
                onDeleteDelayProfilePress={this.onDeleteDelayProfilePress}
              />

              <ConfirmModal
                isOpen={this.state.isDeleteDelayProfileModalOpen}
                kind={kinds.DANGER}
                title={translate('DeleteDelayProfile')}
                message={translate('DeleteDelayProfileMessageText')}
                confirmLabel={translate('Delete')}
                onConfirm={this.onConfirmDeleteDelayProfile}
                onCancel={this.onDeleteDelayProfileModalClose}
              />
            </>
        }
      </div>
    );
  }
}


export default DelayProfile;
