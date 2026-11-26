import { Component } from 'react';
import Label from 'Components/Label';
import IconButton from 'Components/Link/IconButton';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import type { Kind } from 'Helpers/Props/kinds';
import translate from 'Utilities/String/translate';
import type { RenderedTag, TagEdit, TagIdentity, TagSelection } from './tagRenderTypes';
import styles from './TagInputTag.css';

export interface TagInputTagProps<Id extends TagIdentity> {
  index: number;
  tag: RenderedTag<Id>;
  kind: Kind;
  canEdit: boolean;
  isLastTag?: boolean;
  isDisabled?: boolean;
  onDelete: (tag: TagSelection<Id>) => void;
  onEdit: (tag: TagEdit<Id>) => void;
}

class TagInputTag<Id extends TagIdentity> extends Component<TagInputTagProps<Id>> {

  //
  // Listeners

  onDelete = () => {
    const {
      index,
      tag,
      onDelete
    } = this.props;

    onDelete({
      index,
      id: tag.id
    });
  };

  onEdit = () => {
    const {
      index,
      tag,
      onEdit
    } = this.props;

    onEdit({
      index,
      id: tag.id,
      value: tag.name
    });
  };

  //
  // Render

  render() {
    const {
      tag,
      kind,
      canEdit,
      isDisabled
    } = this.props;

    return (
      <div
        className={styles.tag}
        tabIndex={-1}
      >
        <Label
          className={styles.label}
          kind={kind}
        >
          <Link
            className={canEdit ? styles.linkWithEdit : styles.link}
            tabIndex={-1}
            isDisabled={isDisabled}
            aria-label={`${translate('Delete')}: ${tag.name}`}
            onPress={this.onDelete}
          >
            {tag.name}
          </Link>

          {
            canEdit ?
              <div className={styles.editContainer}>
                <IconButton
                  className={styles.editButton}
                  name={icons.EDIT}
                  size={9}
                  aria-label={translate('EditTag')}
                  isDisabled={isDisabled}
                  onPress={this.onEdit}
                />
              </div> :
              null
          }
        </Label>
      </div>
    );
  }
}

export default TagInputTag;
