import { asStateRecord, getItemId, LegacyRootState, StateRecord } from 'Store/StateTypes';
import getSectionState from 'Utilities/State/getSectionState';

function getProviderState(
  payload: StateRecord,
  getState: () => LegacyRootState,
  section: string,
  keyValueOnly = true
) {
  const { id, ...otherPayload } = payload;
  const state = asStateRecord(getSectionState(getState(), section, true));
  const draft = asStateRecord(state.pendingChanges);
  const pendingChanges = { ...draft, ...otherPayload };
  const pendingFields = asStateRecord(draft.fields);
  delete pendingChanges.fields;

  const items: readonly unknown[] = Array.isArray(state.items) ? state.items : [];
  const item = asStateRecord(id ? items.find((value) => getItemId(value) === id) : state.selectedSchema || state.schema);
  if (Array.isArray(item.fields)) {
    const fields: readonly unknown[] = item.fields;
    pendingChanges.fields = fields.flatMap((field) => {
      const record = asStateRecord(field);
      const { name } = record;
      if (typeof name !== 'string') {
        return [];
      }
      const value = Object.hasOwn(pendingFields, name) ? pendingFields[name] : record.value;
      return [keyValueOnly ? { name, value } : { ...record, value }];
    });
  }

  const result = { ...item, ...pendingChanges };
  delete result.presets;
  return result;
}

export default getProviderState;
