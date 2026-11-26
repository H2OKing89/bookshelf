import _ from 'lodash';
import { isStateRecord, type StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';
import updateSectionState from 'Utilities/State/updateSectionState';

type SchemaDefaults = StateRecord | ((schema: StateRecord) => StateRecord);

function selectProviderSchema(state: StateRecord, section: string, payload: unknown, schemaDefaults?: SchemaDefaults) {
  if (!isStateRecord(payload) || typeof payload.implementation !== 'string' ||
      (payload.presetName != null && typeof payload.presetName !== 'string')) {
    return state;
  }
  const { implementation, presetName } = payload;
  const sectionState = getSectionState(state, section);
  const schema: unknown[] = Array.isArray(sectionState.schema) ? sectionState.schema : [];
  const selected = schema.find((value) => isStateRecord(value) && value.implementation === implementation);
  if (!isStateRecord(selected)) {
    return state;
  }
  const presets: unknown[] = Array.isArray(selected.presets) ? selected.presets : [];
  const source = presetName ? presets.find((value) => isStateRecord(value) && value.name === presetName) : selected;
  if (!isStateRecord(source)) {
    return state;
  }
  const copied = _.cloneDeep(source);
  const selectedSchema = typeof schemaDefaults === 'function' ? schemaDefaults(copied) : { ...copied, ...schemaDefaults };
  return updateSectionState(state, section, { ...sectionState, selectedSchema });
}

export default selectProviderSchema;
