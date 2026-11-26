import PropTypes from 'prop-types';
import SelectedMenuItem from './SelectedMenuItem';

function ViewMenuItem(props) {
  const {
    name,
    selectedView,
    ...otherProps
  } = props;

  const isSelected = name === selectedView;

  return (
    <SelectedMenuItem
      name={name}
      isSelected={isSelected}
      {...otherProps}
    />
  );
}

ViewMenuItem.propTypes = {
  name: PropTypes.string,
  selectedView: PropTypes.string.isRequired,
  children: PropTypes.oneOfType([PropTypes.string, PropTypes.element]).isRequired,
  onPress: PropTypes.func.isRequired
};

export default ViewMenuItem;
