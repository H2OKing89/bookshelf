import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import EditQualityProfileModal, { type QualityProfileModalProps } from './EditQualityProfileModal';

function mapStateToProps() {
  return {};
}

const mapDispatchToProps = {
  clearPendingChanges
};

const connector = connect(mapStateToProps, mapDispatchToProps);
type Props = QualityProfileModalProps & ConnectedProps<typeof connector>;

class EditQualityProfileModalConnector extends Component<Props> {

  //
  // Listeners

  onModalClose = () => {
    this.props.clearPendingChanges({ section: 'settings.qualityProfiles' });
    this.props.onModalClose();
  };

  //
  // Render

  render() {
    return (
      <EditQualityProfileModal
        {...this.props}
        onModalClose={this.onModalClose}
      />
    );
  }
}

export default connector(EditQualityProfileModalConnector);
