import { Component } from 'react';
import MenuItem from 'Components/Menu/MenuItem';
import type { SpecificationChoice } from '../../customFormatState';

interface Props {
  name: string;
  implementation: string;
  onPress: (choice: SpecificationChoice) => void;
}

class AddSpecificationPresetMenuItem extends Component<Props> {

  //
  // Listeners

  onPress = () => {
    const {
      name,
      implementation
    } = this.props;

    this.props.onPress({
      name,
      implementation
    });
  };

  //
  // Render

  render() {
    const {
      name
    } = this.props;

    return (
      <MenuItem
        onPress={this.onPress}
      >
        {name}
      </MenuItem>
    );
  }
}

export default AddSpecificationPresetMenuItem;
