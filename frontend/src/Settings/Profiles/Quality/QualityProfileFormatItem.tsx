import { Component } from 'react';
import type { ValueChange } from 'Components/Form/formTypes';
import NumberInput from 'Components/Form/NumberInput';
import styles from './QualityProfileFormatItem.css';

interface Props {
  formatId: number;
  name: string;
  score?: number | null;
  onScoreChange: (id: number, score: number | null) => void;
}

class QualityProfileFormatItem extends Component<Props> {

  //
  // Listeners

  onScoreChange = ({ value }: ValueChange<number | null>) => {
    const {
      formatId
    } = this.props;

    this.props.onScoreChange(formatId, value);
  };

  //
  // Render

  render() {
    const {
      name,
      score = 0
    } = this.props;

    return (
      <div
        className={styles.qualityProfileFormatItemContainer}
      >
        <div
          className={styles.qualityProfileFormatItem}
        >
          <label
            className={styles.formatNameContainer}
          >
            <div className={styles.formatName}>
              {name}
            </div>
            <NumberInput
              className={styles.scoreInput}
              name={name}
              value={score}
              onChange={this.onScoreChange}
            />
          </label>

        </div>
      </div>
    );
  }
}

export default QualityProfileFormatItem;
