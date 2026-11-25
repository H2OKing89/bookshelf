import classNames from 'classnames';
import React from 'react';
import CheckInput from 'Components/Form/CheckInput';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import type { QualityDragEnd, QualityDragHandle, QualityDragMove, QualityItemAllowedChange, QualityProfileGroupItem } from './qualityProfileDragTypes';
import QualityProfileItemDragSource from './QualityProfileItemDragSource';
import styles from './QualityProfileItemGroup.css';

interface QualityProfileItemGroupProps {
  editGroups: boolean;
  groupId: number;
  name: string;
  allowed: boolean;
  items: QualityProfileGroupItem[];
  qualityIndex: string;
  isDragging: boolean;
  isDraggingUp: boolean;
  isDraggingDown: boolean;
  dragHandleRef: QualityDragHandle;
  onItemGroupAllowedChange?: (groupId: number, allowed: boolean) => void;
  onQualityProfileItemAllowedChange: QualityItemAllowedChange;
  onItemGroupNameChange?: (groupId: number, name: string) => void;
  onDeleteGroupPress?: (groupId: number) => void;
  onQualityProfileItemDragMove: QualityDragMove;
  onQualityProfileItemDragEnd: QualityDragEnd;
}

function QualityProfileItemGroup(props: QualityProfileItemGroupProps) {
  const {
    editGroups, groupId, name, allowed, items, qualityIndex,
    isDragging, isDraggingUp, isDraggingDown, dragHandleRef,
    onItemGroupAllowedChange, onQualityProfileItemAllowedChange, onItemGroupNameChange,
    onDeleteGroupPress, onQualityProfileItemDragMove, onQualityProfileItemDragEnd
  } = props;

  return (
    <div
      className={classNames(
        styles.qualityProfileItemGroup,
        editGroups && styles.editGroups,
        isDragging && styles.isDragging
      )}
    >
      <div className={styles.qualityProfileItemGroupInfo}>
        {
          editGroups &&
            <div className={styles.qualityNameContainer}>
              <IconButton
                className={styles.deleteGroupButton}
                name={icons.UNGROUP}
                title={translate('Ungroup')}
                onPress={() => onDeleteGroupPress?.(groupId)}
              />

              <TextInput
                className={styles.nameInput}
                name="name"
                value={name}
                onChange={({ value }: { value: string }) => onItemGroupNameChange?.(groupId, value)}
              />
            </div>
        }

        {
          !editGroups &&
            <label className={styles.qualityNameLabel}>
              <CheckInput
                className={styles.checkInput}
                containerClassName={styles.checkInputContainer}
                name="allowed"
                value={allowed}
                onChange={({ value }: { value: boolean }) => onItemGroupAllowedChange?.(groupId, value)}
              />

              <div className={styles.nameContainer}>
                <div className={classNames(styles.name, !allowed && styles.notAllowed)}>{name}</div>
                <div className={styles.groupQualities}>
                  {
                    items.map(({ quality }) => <Label key={quality.id}>{quality.name}</Label>).reverse()
                  }
                </div>
              </div>
            </label>
        }

        <div ref={dragHandleRef} className={styles.dragHandle}>
          <Icon className={styles.dragIcon} name={icons.REORDER} title={translate('Reorder')} />
        </div>
      </div>

      {
        editGroups &&
          <div className={styles.items}>
            {
              items.map(({ quality }, index) => (
                <QualityProfileItemDragSource
                  key={quality.id}
                  editGroups={editGroups}
                  groupId={groupId}
                  qualityId={quality.id}
                  name={quality.name}
                  allowed={allowed}
                  items={items}
                  qualityIndex={`${qualityIndex}.${index + 1}`}
                  isDragging={isDragging}
                  isDraggingUp={isDraggingUp}
                  isDraggingDown={isDraggingDown}
                  onQualityProfileItemAllowedChange={onQualityProfileItemAllowedChange}
                  onQualityProfileItemDragMove={onQualityProfileItemDragMove}
                  onQualityProfileItemDragEnd={onQualityProfileItemDragEnd}
                />
              )).reverse()
            }
          </div>
      }
    </div>
  );
}

export default QualityProfileItemGroup;
