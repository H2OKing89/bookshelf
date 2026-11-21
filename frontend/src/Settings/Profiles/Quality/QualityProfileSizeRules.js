import PropTypes from 'prop-types';
import React, { useState } from 'react';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import SelectInput from 'Components/Form/SelectInput';
import { inputTypes, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './EditQualityProfileModalContent.css';

const fields = [
  ['minSize', 'ReleaseSizeMinimum'],
  ['preferredSize', 'ReleaseSizePreferred'],
  ['maxSize', 'ReleaseSizeMaximum']
];

function updateSizeRule(items, qualityId, field, value) {
  return items.map((item) => {
    if (item.quality) {
      return item.quality.id === qualityId ? { ...item, [field]: value } : item;
    }

    return { ...item, items: updateSizeRule(item.items, qualityId, field, value) };
  });
}

function QualityProfileSizeRules({ items, onInputChange }) {
  const [qualityId, setQualityId] = useState(null);
  const qualities = items.flatMap((item) => (item.quality ? [item] : item.items))
    .filter((item) => item.sizeUnit);
  const selected = qualities.find((item) => item.quality.id === qualityId) ||
    qualities.find((item) => item.allowed) || qualities[0];

  if (!selected) {
    return null;
  }

  const selectName = 'profileSizeQuality';

  return (
    <details className={styles.sizeRules}>
      <summary>{translate('ReleaseSizeRules')}</summary>
      <p>{translate('ReleaseSizeRulesHelpText')}</p>
      <FormLabel name={selectName} size={sizes.SMALL}>{translate('Quality')}</FormLabel>
      <SelectInput
        id={selectName}
        name={selectName}
        value={selected.quality.id}
        values={qualities.map((item) => ({ key: item.quality.id, value: item.quality.name }))}
        onChange={({ value }) => setQualityId(Number(value))}
      />
      <div className={styles.sizeRuleFields}>
        {fields.map(([field, label]) => {
          const name = `releaseSize-${field}-${selected.quality.id}`;

          return (
            <div key={name}>
              <FormLabel name={name} size={sizes.SMALL}>{translate(label)}</FormLabel>
              <FormInputGroup
                id={name}
                name={name}
                type={inputTypes.NUMBER}
                min={0}
                max={1000000}
                step={0.1}
                isFloat={true}
                value={selected[field] || null}
                unit={selected.sizeUnit}
                onChange={({ value }) => onInputChange({
                  name: 'items',
                  value: updateSizeRule(items, selected.quality.id, field, value ?? 0)
                })}
              />
            </div>
          );
        })}
      </div>
    </details>
  );
}

QualityProfileSizeRules.propTypes = {
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  onInputChange: PropTypes.func.isRequired
};

export default QualityProfileSizeRules;
