import { Component, type ComponentPropsWithRef, type ComponentType, type MouseEvent } from 'react';
import type { Kind } from 'Helpers/Props/kinds';
import type { TagInputTagProps } from './TagInputTag';
import type { RenderedTag, TagEdit, TagIdentity, TagSelection } from './tagRenderTypes';
import styles from './TagInputInput.css';

interface Props<Id extends TagIdentity> {
  forwardedRef?: (element: HTMLElement | null) => void;
  className?: string;
  tags: readonly RenderedTag<Id>[];
  inputProps: ComponentPropsWithRef<'input'>;
  kind: Kind;
  isFocused: boolean;
  canEdit: boolean;
  isDisabled?: boolean;
  tagComponent: ComponentType<TagInputTagProps<Id>>;
  onTagDelete: (tag: TagSelection<Id>) => void;
  onTagEdit: (tag: TagEdit<Id>) => void;
  onInputContainerPress: () => void;
}

class TagInputInput<Id extends TagIdentity> extends Component<Props<Id>> {

  onMouseDown = (event: MouseEvent<HTMLDivElement>) => {
    event.preventDefault();

    const {
      isFocused,
      isDisabled,
      onInputContainerPress
    } = this.props;

    if (isFocused || isDisabled) {
      return;
    }

    onInputContainerPress();
  };

  render() {
    const {
      forwardedRef,
      className = styles.inputContainer,
      tags,
      inputProps,
      kind,
      canEdit,
      isDisabled,
      tagComponent: TagComponent,
      onTagDelete,
      onTagEdit
    } = this.props;

    return (
      <div
        ref={forwardedRef}
        className={className}
        onMouseDown={this.onMouseDown}
      >
        {
          tags.map((tag, index) => {
            return (
              <TagComponent
                key={`${typeof tag.id}:${tag.id}`}
                index={index}
                tag={tag}
                kind={kind}
                canEdit={canEdit}
                isDisabled={isDisabled}
                isLastTag={index === tags.length - 1}
                onDelete={onTagDelete}
                onEdit={onTagEdit}
              />
            );
          })
        }

        <input {...inputProps} />
      </div>
    );
  }
}

export default TagInputInput;
