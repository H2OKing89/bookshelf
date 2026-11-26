import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import { cloneQualityProfile, deleteQualityProfile, fetchQualityProfiles } from 'Store/Actions/settingsActions';
import { asStateRecord, type LegacyRootState } from 'Store/StateTypes';
import sortByName from 'Utilities/Array/sortByName';
import QualityProfiles from './QualityProfiles';
import { parseProfileSummary, type QualityProfileSummary } from './qualityProfileSettingsState';

function createMapStateToProps() {
  return createSelector(
    (state: LegacyRootState) => state.settings.qualityProfiles,
    (value) => {
      const section = asStateRecord(value);
      const items = Array.isArray(section.items) ? section.items.map(parseProfileSummary).filter((item): item is QualityProfileSummary => item !== null).sort(sortByName) : [];
      return {
        items,
        isFetching: section.isFetching === true,
        isPopulated: section.isPopulated === true,
        isDeleting: section.isDeleting === true,
        error: section.error
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchQualityProfiles: fetchQualityProfiles,
  dispatchDeleteQualityProfile: deleteQualityProfile,
  dispatchCloneQualityProfile: cloneQualityProfile as unknown as (payload: { id: number }) => { type: string; payload?: unknown }
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = ConnectedProps<typeof connector>;

class QualityProfilesConnector extends Component<Props> {

  //
  // Lifecycle

  componentDidMount() {
    this.props.dispatchFetchQualityProfiles();
  }

  //
  // Listeners

  onConfirmDeleteQualityProfile = (id: number) => {
    this.props.dispatchDeleteQualityProfile({ id });
  };

  onCloneQualityProfilePress = (id: number) => {
    this.props.dispatchCloneQualityProfile({ id });
  };

  //
  // Render

  render() {
    return (
      <QualityProfiles
        onConfirmDeleteQualityProfile={this.onConfirmDeleteQualityProfile}
        onCloneQualityProfilePress={this.onCloneQualityProfilePress}
        {...this.props}
      />
    );
  }
}

export default connector(QualityProfilesConnector);
