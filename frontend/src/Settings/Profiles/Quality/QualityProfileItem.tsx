import classNames from 'classnames';
import CheckInput from 'Components/Form/CheckInput';
import Icon from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import type { QualityDragHandle, QualityItemAllowedChange } from './qualityProfileDragTypes';
import styles from './QualityProfileItem.css';

interface QualityProfileItemProps {
  editGroups?: boolean;
  isPreview?: boolean;
  groupId?: number | null;
  qualityId: number;
  name: string;
  allowed: boolean;
  isDragging: boolean;
  dragHandleRef?: QualityDragHandle;
  onCreateGroupPress?: (qualityId: number) => void;
  onQualityProfileItemAllowedChange?: QualityItemAllowedChange;
}

function QualityProfileItem(props: QualityProfileItemProps) {
  const {
    editGroups,
    isPreview = false,
    groupId,
    qualityId,
    name,
    allowed,
    isDragging,
    dragHandleRef,
    onCreateGroupPress,
    onQualityProfileItemAllowedChange
  } = props;

  return (
    <div
      className={classNames(
        styles.qualityProfileItem,
        isDragging && styles.isDragging,
        isPreview && styles.isPreview,
        groupId != null && styles.isInGroup
      )}
    >
      <label className={styles.qualityNameContainer}>
        {
          editGroups && groupId == null && !isPreview &&
            <IconButton
              className={styles.createGroupButton}
              name={icons.GROUP}
              title={translate('Group')}
              onPress={() => onCreateGroupPress?.(qualityId)}
            />
        }

        {
          !editGroups &&
            <CheckInput
              className={styles.checkInput}
              containerClassName={styles.checkInputContainer}
              name={name}
              value={allowed}
              isDisabled={groupId != null || isPreview}
              onChange={({ value }: { value: boolean }) => onQualityProfileItemAllowedChange?.(qualityId, value)}
            />
        }

        <div
          className={classNames(
            styles.qualityName,
            groupId != null && styles.isInGroup,
            !allowed && styles.notAllowed
          )}
        >
          {name}
        </div>
      </label>

      <div ref={dragHandleRef} className={styles.dragHandle}>
        <Icon
          className={styles.dragIcon}
          title={translate('Reorder')}
          name={icons.REORDER}
        />
      </div>
    </div>
  );
}

export default QualityProfileItem;
