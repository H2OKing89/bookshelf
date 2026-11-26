import * as Slider from '@radix-ui/react-slider';
import React, { Component } from 'react';
import type { ValueChange } from 'Components/Form/formTypes';
import NumberInput from 'Components/Form/NumberInput';
import TextInput, { type TextInputChange } from 'Components/Form/TextInput';
import Label from 'Components/Label';
import Popover from 'Components/Tooltip/Popover';
import { kinds, tooltipPositions } from 'Helpers/Props';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import QualityDefinitionLimits from './QualityDefinitionLimits';
import {
  MAXIMUM_SIZE, MINIMUM_SIZE, normalizeSize, type QualitySizes,
  SIZE_DISTANCE, sizesFromSlider, SLIDER_MAX, SLIDER_MIN,
  SLIDER_STEP, sliderStepsBetween, sliderThumbBounds, sliderValues
} from './qualitySizeSlider';
import styles from './QualityDefinition.css';

interface QualityDefinitionProps extends QualitySizes {
  id: number;
  quality: { name: string };
  title: string;
  advancedSettings: boolean;
  onTitleChange: (change: TextInputChange) => void;
  onSizeChange: (limits: QualitySizes) => void;
}

class QualityDefinition extends Component<QualityDefinitionProps> {

  onSliderChange = (values: number[]) => {
    const limits = sizesFromSlider(values, this.props);

    if (limits) {
      this.props.onSizeChange(limits);
    }
  };

  onThumbKeyDown = (event: React.KeyboardEvent<HTMLSpanElement>, index: 0 | 1) => {
    if (event.key !== 'Home' && event.key !== 'End') {
      return;
    }

    // Home/End adjust the focused thumb within its own legal range,
    // not the first or last thumb indiscriminately.
    event.preventDefault();
    const values = sliderValues(this.props);
    const bounds = sliderThumbBounds(values)[index];
    values[index] = bounds[event.key === 'Home' ? 0 : 1];
    this.onSliderChange(values);
  };

  onMinKeyDown = (event: React.KeyboardEvent<HTMLSpanElement>) => this.onThumbKeyDown(event, 0);

  onMaxKeyDown = (event: React.KeyboardEvent<HTMLSpanElement>) => this.onThumbKeyDown(event, 1);

  onMinSizeChange = ({ value }: ValueChange<number | null>) => {
    this.props.onSizeChange({
      minSize: normalizeSize(value),
      maxSize: this.props.maxSize
    });
  };

  onMaxSizeChange = ({ value }: ValueChange<number | null>) => {
    this.props.onSizeChange({
      minSize: this.props.minSize,
      maxSize: value === MAXIMUM_SIZE ? null : normalizeSize(value)
    });
  };

  //
  // Render

  render() {
    const {
      id,
      quality,
      title,
      minSize,
      maxSize,
      advancedSettings,
      onTitleChange
    } = this.props;

    const values = sliderValues(this.props);
    const [minimumBounds, maximumBounds] = sliderThumbBounds(values);
    const minBytes = (minSize ?? MINIMUM_SIZE) * 128;
    const maxBytes = maxSize && maxSize * 128;

    const minRate = `${formatBytes(minBytes, true)}/s`;
    const maxRate = maxBytes ? `${formatBytes(maxBytes, true)}/s` : 'Unlimited';

    return (
      <div className={styles.qualityDefinition}>
        <div className={styles.quality}>
          {quality.name}
        </div>

        <div className={styles.title}>
          <TextInput
            name={`${id}.${title}`}
            value={title}
            onChange={onTitleChange}
          />
        </div>

        <div className={styles.sizeLimit}>
          <Slider.Root
            min={SLIDER_MIN}
            max={SLIDER_MAX}
            step={SLIDER_STEP}
            minStepsBetweenThumbs={sliderStepsBetween(values)}
            preserveThumbOrder={true}
            value={values}
            className={styles.slider}
            onValueChange={this.onSliderChange}
          >
            <Slider.Track className={styles.bar}>
              <Slider.Range className={styles.range} />
            </Slider.Track>
            <Slider.Thumb
              className={styles.handle}
              aria-label={`${quality.name} ${translate('ReleaseSizeMinimum')}`}
              aria-valuemin={minimumBounds[0]}
              aria-valuemax={minimumBounds[1]}
              aria-valuetext={minRate}
              onKeyDown={this.onMinKeyDown}
            />
            <Slider.Thumb
              className={styles.handle}
              aria-label={`${quality.name} ${translate('ReleaseSizeMaximum')}`}
              aria-valuemin={maximumBounds[0]}
              aria-valuemax={maximumBounds[1]}
              aria-valuetext={maxRate}
              onKeyDown={this.onMaxKeyDown}
            />
          </Slider.Root>

          <div className={styles.sizes}>
            <div>
              <Popover
                anchor={
                  <Label kind={kinds.INFO}>{minRate}</Label>
                }
                title={translate('MinimumLimits')}
                body={
                  <QualityDefinitionLimits
                    bytes={minBytes}
                    message={translate('NoMinimumForAnyRuntime')}
                  />
                }
                position={tooltipPositions.BOTTOM}
              />
            </div>

            <div>
              <Popover
                anchor={
                  <Label kind={kinds.WARNING}>{maxRate}</Label>
                }
                title={translate('MaximumLimits')}
                body={
                  <QualityDefinitionLimits
                    bytes={maxBytes}
                    message={translate('NoLimitForAnyRuntime')}
                  />
                }
                position={tooltipPositions.BOTTOM}
              />
            </div>
          </div>
        </div>

        {
          advancedSettings &&
            <div className={styles.kilobitsPerSecond}>
              <div>
                Min

                <NumberInput
                  className={styles.sizeInput}
                  name={`${id}.min`}
                  value={minSize ?? MINIMUM_SIZE}
                  min={MINIMUM_SIZE}
                  max={maxSize ? maxSize - SIZE_DISTANCE : MAXIMUM_SIZE - SIZE_DISTANCE}
                  step={0.1}
                  isFloat={true}
                  onChange={this.onMinSizeChange}
                />
              </div>

              <div>
                Max

                <NumberInput
                  className={styles.sizeInput}
                  name={`${id}.max`}
                  value={maxSize || MAXIMUM_SIZE}
                  min={(minSize ?? MINIMUM_SIZE) + SIZE_DISTANCE}
                  max={MAXIMUM_SIZE}
                  step={0.1}
                  isFloat={true}
                  onChange={this.onMaxSizeChange}
                />
              </div>
            </div>
        }
      </div>
    );
  }
}

export default QualityDefinition;
