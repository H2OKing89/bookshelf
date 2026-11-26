import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';
import TagInput from './TagInput';
import { numericTagProjection, removeTag, type TagOption, type TagRemoval } from './tagInputState';

interface OwnProps extends FormControlProps {
  name: string;
  isDisabled?: boolean;
  value: number[];
  values?: readonly unknown[];
  onChange: (change: ValueChange<number[]>) => void;
}

function createMapStateToProps() {
  return createSelector(
    (_state: unknown, { value }: OwnProps) => value,
    (_state: unknown, { values }: OwnProps) => values,
    (tags, tagList) => numericTagProjection(tags, tagList, 'option')
  );
}

const connector = connect(createMapStateToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class TagSelectInputConnector extends Component<Props> {

  //
  // Listeners

  onTagAdd = (tag: TagOption) => {
    const {
      name,
      value,
      allTags
    } = this.props;

    if (typeof tag.id === 'number' && !value.includes(tag.id) && allTags.some((candidate) => candidate.id === tag.id)) {
      this.props.onChange({ name, value: [...value, tag.id] });
    }
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

export default connector(TagSelectInputConnector);
