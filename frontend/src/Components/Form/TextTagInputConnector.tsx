
import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import split from 'Utilities/String/split';
import type { FormControlProps } from './formControlProps';
import type { ValueChange } from './formTypes';
import TagInput from './TagInput';
import { removeTag, type TagOption, type TagRemoval,textTags } from './tagInputState';

interface OwnProps extends FormControlProps {
  name: string;
  value?: unknown;
  onChange: (change: ValueChange<string[]>) => void;
}

function createMapStateToProps() {
  return createSelector(
    (_state: unknown, { value }: OwnProps) => value,
    (tags) => {
      const tagsArray = textTags(tags);
      return { tags: tagsArray.map((name) => ({ id: name, name })), valueArray: tagsArray };
    }
  );
}

const connector = connect(createMapStateToProps);
type Props = OwnProps & ConnectedProps<typeof connector>;

class TextTagInputConnector extends Component<Props> {

  //
  // Listeners

  onTagAdd = (tag: TagOption) => {
    const {
      name,
      valueArray,
      onChange
    } = this.props;

    // Split and trim tags before adding them to the list, this will
    // cleanse tags pasted in that had commas and spaces which leads
    // to oddities with restrictions (as an example).

    const newValue = [...valueArray];
    const newTags = tag.name.startsWith('/') ? [tag.name] : split(tag.name);

    newTags.forEach((newTag) => {
      newValue.push(newTag.trim());
    });

    onChange({ name, value: newValue });
  };

  onTagDelete = (removal: TagRemoval) => {
    const {
      name,
      valueArray,
      onChange
    } = this.props;

    const newValue = removeTag(valueArray, removal);

    onChange({
      name,
      value: newValue
    });
  };

  onTagReplace = (tagToReplace: TagRemoval, newTag: TagOption) => {
    const {
      name,
      valueArray,
      onChange
    } = this.props;

    const newValue = removeTag(valueArray, tagToReplace);
    newValue.push(newTag.name.trim());

    onChange({ name, value: newValue });
  };

  //
  // Render

  render() {
    return (
      <TagInput
        delimiters={['Tab', 'Enter', ',']}
        tagList={[]}
        onTagAdd={this.onTagAdd}
        onTagDelete={this.onTagDelete}
        onTagReplace={this.onTagReplace}
        {...this.props}
      />
    );
  }
}

export default connector(TextTagInputConnector);
