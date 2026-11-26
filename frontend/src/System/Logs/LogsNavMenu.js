import PropTypes from 'prop-types';
import Menu from 'Components/Menu/Menu';
import MenuButton from 'Components/Menu/MenuButton';
import MenuContent from 'Components/Menu/MenuContent';
import MenuItem from 'Components/Menu/MenuItem';

function LogsNavMenu({ current }) {
  return (
    <Menu>
      <MenuButton aria-label={current}>
        {current}
      </MenuButton>
      <MenuContent>
        <MenuItem to="/system/logs/files">
          Log Files
        </MenuItem>
        <MenuItem to="/system/logs/files/update">
          Updater Log Files
        </MenuItem>
      </MenuContent>
    </Menu>
  );
}

LogsNavMenu.propTypes = {
  current: PropTypes.string.isRequired
};

export default LogsNavMenu;
