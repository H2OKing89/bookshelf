import { Component } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import type { FormMessage } from 'Components/Form/formTypes';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Measure from 'Components/Measure';
import { icons, kinds, sizes } from 'Helpers/Props';
import type { QualityDragEnd, QualityDragMove } from './qualityProfileDragTypes';
import QualityProfileItemDragPreview from './QualityProfileItemDragPreview';
import QualityProfileItemDragSource from './QualityProfileItemDragSource';
import { isQualityLeaf, qualityItemId, qualityItemName, type QualityProfileItem } from './qualityProfileSettingsState';
import styles from './QualityProfileItems.css';

export interface QualityItemEditorProps {
  dragQualityIndex: string | null;
  dropQualityIndex: string | null;
  dropPosition: 'above' | 'below' | null;
  onCreateGroupPress: (id: number) => void;
  onDeleteGroupPress: (id: number) => void;
  onQualityProfileItemAllowedChange: (id: number, allowed: boolean) => void;
  onItemGroupAllowedChange: (id: number, allowed: boolean) => void;
  onItemGroupNameChange: (id: number, name: string) => void;
  onQualityProfileItemDragMove: QualityDragMove;
  onQualityProfileItemDragEnd: QualityDragEnd;
  onToggleEditGroupsMode: () => void;
}

interface Props extends QualityItemEditorProps {
  editGroups: boolean;
  qualityProfileItems: readonly QualityProfileItem[];
  errors?: readonly FormMessage[];
  warnings?: readonly FormMessage[];
}
interface State {
  qualitiesHeight: number;
  qualitiesHeightEditGroups: number;
}

class QualityProfileItems extends Component<Props, State> {

  //
  // Lifecycle

  constructor(props: Props) {
    super(props);

    this.state = {
      qualitiesHeight: 0,
      qualitiesHeightEditGroups: 0
    };
  }

  //
  // Listeners

  onMeasure = ({ height }: { height: number }) => {
    if (this.props.editGroups) {
      this.setState({
        qualitiesHeightEditGroups: height
      });
    } else {
      this.setState({ qualitiesHeight: height });
    }
  };

  onToggleEditGroupsMode = () => {
    this.props.onToggleEditGroupsMode();
  };

  //
  // Render

  render() {
    const {
      editGroups,
      dropQualityIndex,
      dropPosition,
      qualityProfileItems,
      errors = [],
      warnings = [],
      ...otherProps
    } = this.props;

    const {
      qualitiesHeight,
      qualitiesHeightEditGroups
    } = this.state;

    const isDragging = dropQualityIndex !== null;
    const isDraggingUp = isDragging && dropPosition === 'above';
    const isDraggingDown = isDragging && dropPosition === 'below';
    const minHeight = editGroups ? qualitiesHeightEditGroups : qualitiesHeight;

    return (
      <FormGroup size={sizes.EXTRA_SMALL}>
        <FormLabel size={sizes.SMALL}>
          Qualities
        </FormLabel>

        <div>
          <FormInputHelpText
            text="Qualities higher in the list are more preferred. Qualities within the same group are equal. Only checked qualities are wanted"
          />

          {
            errors.map((error, index) => {
              return (
                <FormInputHelpText
                  key={index}
                  text={error.message}
                  isError={true}
                  isCheckInput={false}
                />
              );
            })
          }

          {
            warnings.map((warning, index) => {
              return (
                <FormInputHelpText
                  key={index}
                  text={warning.message}
                  isWarning={true}
                  isCheckInput={false}
                />
              );
            })
          }

          <Button
            className={styles.editGroupsButton}
            kind={kinds.PRIMARY}
            onPress={this.onToggleEditGroupsMode}
          >
            <div>
              <Icon
                className={styles.editGroupsButtonIcon}
                name={editGroups ? icons.REORDER : icons.GROUP}
              />

              {
                editGroups ? 'Done Editing Groups' : 'Edit Groups'
              }
            </div>
          </Button>

          <Measure
            includeMargin={false}
            onMeasure={this.onMeasure}
            className={styles.qualities}
            style={{ minHeight: `${minHeight}px` }}
          >
            {
              qualityProfileItems.map((item, index) => {
                const leaf = isQualityLeaf(item);

                return (
                  <QualityProfileItemDragSource
                    key={qualityItemId(item)}
                    editGroups={editGroups}
                    groupId={leaf ? undefined : item.id}
                    qualityId={leaf ? item.quality.id : undefined}
                    name={qualityItemName(item)}
                    allowed={item.allowed}
                    items={leaf ? undefined : item.items}
                    qualityIndex={`${index + 1}`}
                    isDragging={isDragging}
                    isDraggingUp={isDraggingUp}
                    isDraggingDown={isDraggingDown}
                    {...otherProps}
                  />
                );
              }).reverse()
            }

            <QualityProfileItemDragPreview />
          </Measure>
        </div>
      </FormGroup>
    );
  }
}

export default QualityProfileItems;
