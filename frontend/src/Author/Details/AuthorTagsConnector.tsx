import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createTagsSelector from 'Store/Selectors/createTagsSelector';
import AuthorTags from './AuthorTags';

function createMapStateToProps() {
  return createSelector(
    createAuthorSelector(),
    createTagsSelector(),
    (author, tagList) => ({
      tags: (author?.tags ?? []).flatMap((tagId) => {
        const tag = tagList.find((item) => item.id === tagId);
        return tag ? [tag.label] : [];
      }).sort((a, b) => a.localeCompare(b))
    })
  );
}

export default connect(createMapStateToProps)(AuthorTags);
