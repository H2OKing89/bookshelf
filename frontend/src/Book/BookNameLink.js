import PropTypes from 'prop-types';
import React from 'react';
import Link from 'Components/Link/Link';

function BookNameLink({ titleSlug, title, displayTitle }) {
  const link = `/book/${titleSlug}`;

  return (
    <Link to={link} title={title}>
      {displayTitle || title}
    </Link>
  );
}

BookNameLink.propTypes = {
  titleSlug: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  displayTitle: PropTypes.string
};

export default BookNameLink;
