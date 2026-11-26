import _ from 'lodash';
import { Component } from 'react';
import { connect, type ConnectedProps } from 'react-redux';
import { createSelector } from 'reselect';
import type { ValueChange } from 'Components/Form/formTypes';
import { fetchQualityProfileSchema, saveQualityProfile, setQualityProfileValue } from 'Store/Actions/settingsActions';
import createProfileInUseSelector from 'Store/Selectors/createProfileInUseSelector';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import EditQualityProfileModalContent from './EditQualityProfileModalContent';
import { adjustQualityDropMove, type QualityProfileDragMove } from './qualityProfileDragTypes';
import { isQualityLeaf, moveQualityItem, nextGroupId, type ProfileFormatItem, qualityItemId, qualityOptions, qualityProfileDrafts, type QualityProfileItem,safeCutoff } from './qualityProfileSettingsState';

export interface QualityProfileEditorProps {
  id?: number;
  onContentHeightChange: (height: number) => void;
  onModalClose: () => void;
  onDeleteQualityProfilePress?: () => void;
}

function createMapStateToProps() {
  return createSelector(
    createProviderSettingsSelector('qualityProfiles'),
    createProfileInUseSelector('qualityProfileId'),
    (qualityProfile, isInUse) => {
      const profile = qualityProfileDrafts(qualityProfile.item);
      return {
        ...qualityProfile,
        profile,
        qualities: qualityOptions(profile.items.value),
        isInUse
      };
    }
  );
}

const mapDispatchToProps = {
  fetchQualityProfileSchema,
  saveQualityProfile,
  setQualityProfileValue
};

const connector = connect(createMapStateToProps, mapDispatchToProps);
type Props = QualityProfileEditorProps & ConnectedProps<typeof connector>;

interface State {
  dragQualityIndex: string | null;
  dropQualityIndex: string | null;
  dropPosition: 'above' | 'below' | null;
  editGroups: boolean;
}

export class EditQualityProfileModalContentConnector extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = {
      dragQualityIndex: null,
      dropQualityIndex: null,
      dropPosition: null,
      editGroups: false
    };
  }

  componentDidMount() {
    if (!this.props.id && !this.props.isPopulated) {
      this.props.fetchQualityProfileSchema();
    }
  }

  componentDidUpdate(prevProps: Props) {
    if (prevProps.isSaving && !this.props.isSaving && !this.props.saveError) {
      this.props.onModalClose();
    }
  }

  updateItems = (change: (items: QualityProfileItem[]) => boolean) => {
    const { profile } = this.props;
    if (!profile.valid) {
      return;
    }
    const items = _.cloneDeep(profile.items.value);
    if (!change(items)) {
      return;
    }
    this.props.setQualityProfileValue({ name: 'items', value: items });
    const cutoff = safeCutoff(items, profile.cutoff.value);
    if (cutoff !== profile.cutoff.value) {
      this.props.setQualityProfileValue({ name: 'cutoff', value: cutoff });
    }
  };

  onInputChange = (change: ValueChange<unknown>) => {
    if (this.props.profile.valid) {
      this.props.setQualityProfileValue(change);
    }
  };

  onCutoffChange = ({ name, value }: ValueChange<string | number>) => {
    const id = Number(value);
    if (Number.isSafeInteger(id) && this.props.profile.items.value.some((item) => item.allowed && qualityItemId(item) === id)) {
      this.props.setQualityProfileValue({ name, value: id });
    }
  };

  onSavePress = () => {
    if (this.props.profile.valid) {
      this.props.saveQualityProfile({ id: this.props.id });
    }
  };

  onQualityProfileItemAllowedChange = (id: number, allowed: boolean) => {
    this.updateItems((items) => {
      const item = items.find((candidate) => isQualityLeaf(candidate) && candidate.quality.id === id);
      if (!item) {
        return false;
      }
      item.allowed = allowed;
      return true;
    });
  };

  onQualityProfileFormatItemScoreChange = (id: number, score: number | null) => {
    if (!this.props.profile.valid) {
      return;
    }
    const items: ProfileFormatItem[] = _.cloneDeep(this.props.profile.formatItems.value);
    const item = items.find((candidate) => candidate.format === id);
    if (!item) {
      return;
    }
    item.score = score;
    this.props.setQualityProfileValue({ name: 'formatItems', value: items });
  };

  onItemGroupAllowedChange = (id: number, allowed: boolean) => {
    this.updateItems((items) => {
      const group = items.find((candidate) => !isQualityLeaf(candidate) && candidate.id === id);
      if (!group || isQualityLeaf(group)) {
        return false;
      }
      group.allowed = allowed;
      group.items.forEach((item) => { item.allowed = allowed; });
      return true;
    });
  };

  onItemGroupNameChange = (id: number, name: string) => {
    this.updateItems((items) => {
      const group = items.find((candidate) => !isQualityLeaf(candidate) && candidate.id === id);
      if (!group || isQualityLeaf(group)) {
        return false;
      }
      group.name = name;
      return true;
    });
  };

  onCreateGroupPress = (id: number) => {
    this.updateItems((items) => {
      const index = items.findIndex((candidate) => isQualityLeaf(candidate) && candidate.quality.id === id);
      const item = items[index];
      if (!item || !isQualityLeaf(item)) {
        return false;
      }
      items.splice(index, 1, { id: nextGroupId(items), name: item.quality.name, allowed: item.allowed, items: [item] });
      return true;
    });
  };

  onDeleteGroupPress = (id: number) => {
    this.updateItems((items) => {
      const index = items.findIndex((candidate) => !isQualityLeaf(candidate) && candidate.id === id);
      const group = items[index];
      if (!group || isQualityLeaf(group)) {
        return false;
      }
      items.splice(index, 1, ...group.items);
      return true;
    });
  };

  onQualityProfileItemDragMove = (move: QualityProfileDragMove) => {
    const adjusted = adjustQualityDropMove(move);
    const dragQualityIndex = adjusted?.dragQualityIndex ?? null;
    const dropQualityIndex = adjusted?.dropQualityIndex ?? null;
    const dropPosition = adjusted?.dropPosition ?? null;

    if (dragQualityIndex !== this.state.dragQualityIndex || dropQualityIndex !== this.state.dropQualityIndex || dropPosition !== this.state.dropPosition) {
      this.setState({ dragQualityIndex, dropQualityIndex, dropPosition });
    }
  };

  onQualityProfileItemDragEnd = (move: QualityProfileDragMove | null) => {
    // Hover feedback is visual state only. End can occur in the same batch.
    if (move) {
      this.updateItems((items) => moveQualityItem(items, move));
    }
    this.setState({ dragQualityIndex: null, dropQualityIndex: null, dropPosition: null });
  };

  onToggleEditGroupsMode = () => {
    this.setState((state) => ({ editGroups: !state.editGroups }));
  };

  render() {
    return (
      <EditQualityProfileModalContent
        {...this.state}
        {...this.props}
        onSavePress={this.onSavePress}
        onInputChange={this.onInputChange}
        onCutoffChange={this.onCutoffChange}
        onCreateGroupPress={this.onCreateGroupPress}
        onDeleteGroupPress={this.onDeleteGroupPress}
        onQualityProfileItemAllowedChange={this.onQualityProfileItemAllowedChange}
        onItemGroupAllowedChange={this.onItemGroupAllowedChange}
        onItemGroupNameChange={this.onItemGroupNameChange}
        onQualityProfileItemDragMove={this.onQualityProfileItemDragMove}
        onQualityProfileItemDragEnd={this.onQualityProfileItemDragEnd}
        onQualityProfileFormatItemScoreChange={this.onQualityProfileFormatItemScoreChange}
        onToggleEditGroupsMode={this.onToggleEditGroupsMode}
      />
    );
  }
}

export default connector(EditQualityProfileModalContentConnector);
