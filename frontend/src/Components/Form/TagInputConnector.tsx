import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { addTag } from 'Store/Actions/tagActions';
import createTagsSelector from 'Store/Selectors/createTagsSelector';
import type { FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';
import TagInput from './TagInput';
import { numericTagProjection, removeTag, type TagOption, type TagRemoval } from './tagInputState';

interface OwnProps extends FormControlProps {
  name: string;
  value: number[];
  values?: readonly unknown[];
  onChange: (change: ValueChange<number[]>) => void;
}

const validTagRegex = new RegExp('[^-_a-z0-9]', 'i');

function isValidTag(tagName: string) {
  try {
    return !validTagRegex.test(tagName);
  } catch {
    return false;
  }
}

function createMapStateToProps() {
  return createSelector(
    (_state: unknown, { value }: OwnProps) => value,
    createTagsSelector(),
    (tags, tagList) => numericTagProjection(tags, tagList, 'store')
  );
}

const mapDispatchToProps = {
  addTag
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class TagInputConnector extends Component<Props> {

  //
  // Listeners

  onTagAdd = (tag: TagOption) => {
    const {
      name,
      value,
      allTags
    } = this.props;

    if (tag.id === undefined) {
      const existingTag =allTags.some((candidate) => candidate.name === tag.name);

      if (isValidTag(tag.name) && !existingTag) {
        this.props.addTag({
          tag: { label: tag.name },
          onTagCreated: this.onTagCreated
        });
      }

      return;
    }

    if (typeof tag.id !== 'number' || value.includes(tag.id)) {
      return;
    }
    const newValue = [...value, tag.id];

    this.props.onChange({ name, value: newValue });
  };

  onTagDelete = (removal: TagRemoval) => {
    const {
      name,
      value
    } = this.props;

    const newValue = removeTag(value, removal);

    this.props.onChange({
      name,
      value: newValue
    });
  };

  onTagCreated = (tag: { id: number }) => {
    const {
      name,
      value
    } = this.props;

    if (typeof tag.id !== 'number' || value.includes(tag.id)) {
      return;
    }
    const newValue = [...value, tag.id];

    this.props.onChange({ name, value: newValue });
  };

  //
  // Render

  render() {
    return (
      <TagInput
        onTagAdd={this.onTagAdd}
        onTagDelete={this.onTagDelete}
        {...this.props}
      />
    );
  }
}

export default connector(TagInputConnector);
