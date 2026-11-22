import PropTypes from 'prop-types';
import React from 'react';
import { connect } from 'react-redux';
import { toggleAdvancedSettings } from 'Store/Actions/settingsActions';
import SettingsToolbar from './SettingsToolbar';
import useUnsavedNavigation from './useUnsavedNavigation';

function mapStateToProps(state) {
  return {
    advancedSettings: state.settings.advancedSettings
  };
}

function SettingsToolbarConnector({ hasPendingChanges = false, onDiscardPendingChanges, ...props }) {
  useUnsavedNavigation(hasPendingChanges, onDiscardPendingChanges);

  return <SettingsToolbar {...props} hasPendingChanges={hasPendingChanges} />;
}

SettingsToolbarConnector.propTypes = {
  hasPendingChanges: PropTypes.bool,
  onDiscardPendingChanges: PropTypes.func
};

export default connect(mapStateToProps, { onAdvancedSettingsPress: toggleAdvancedSettings })(SettingsToolbarConnector);
