import PropTypes from 'prop-types';
import translate from 'Utilities/String/translate';

function WantedBookDetails({ isQueued, qualities }) {
  const items = [...(qualities ?? [])];
  if (isQueued) {
    items.push(translate('Queued'));
  }

  return items.length ? <div>{items.join(' · ')}</div> : null;
}

WantedBookDetails.propTypes = {
  isQueued: PropTypes.bool.isRequired,
  qualities: PropTypes.arrayOf(PropTypes.string)
};

export default WantedBookDetails;
