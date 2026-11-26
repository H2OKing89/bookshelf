import { Component } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import Menu from 'Components/Menu/Menu';
import MenuContent from 'Components/Menu/MenuContent';
import { sizes } from 'Helpers/Props';
import type { SpecificationChoice, SpecificationSchema } from '../../customFormatState';
import AddSpecificationPresetMenuItem from './AddSpecificationPresetMenuItem';
import styles from './AddSpecificationItem.css';

interface Props extends SpecificationSchema {
  onSpecificationSelect: (choice: SpecificationChoice) => void;
}

class AddSpecificationItem extends Component<Props> {

  //
  // Listeners

  onSpecificationSelect = () => {
    const {
      implementation
    } = this.props;

    this.props.onSpecificationSelect({ implementation });
  };

  //
  // Render

  render() {
    const {
      implementation,
      implementationName,
      infoLink,
      presets,
      onSpecificationSelect
    } = this.props;

    const hasPresets = presets.length > 0;

    return (
      <div
        className={styles.specification}
      >
        <Link
          className={styles.underlay}
          onPress={this.onSpecificationSelect}
        />

        <div className={styles.overlay}>
          <div className={styles.name}>
            {implementationName}
          </div>

          <div className={styles.actions}>
            {
              hasPresets &&
                <span>
                  <Button
                    size={sizes.SMALL}
                    onPress={this.onSpecificationSelect}
                  >
                    Custom
                  </Button>

                  <Menu className={styles.presetsMenu}>
                    <Button
                      className={styles.presetsMenuButton}
                      size={sizes.SMALL}
                    >
                      Presets
                    </Button>

                    <MenuContent>
                      {
                        presets.map((preset, index) => {
                          return (
                            <AddSpecificationPresetMenuItem
                              key={index}
                              name={preset.name}
                              implementation={implementation}
                              onPress={onSpecificationSelect}
                            />
                          );
                        })
                      }
                    </MenuContent>
                  </Menu>
                </span>
            }

            <Button
              to={infoLink}
              size={sizes.SMALL}
            >
              More Info
            </Button>
          </div>
        </div>
      </div>
    );
  }
}

export default AddSpecificationItem;
