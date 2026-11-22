import { isPreferenceRecord } from './preferenceStorage';

interface TableColumn {
  name: string;
  isVisible: boolean;
  isModifiable?: boolean;
}

export default function mergeColumnPreferences<Column extends TableColumn>(defaults: Column[], saved: unknown): Column[] {
  if (!Array.isArray(saved)) {
    return defaults;
  }

  const byName = new Map(defaults.map((column) => [column.name, column]));
  const applied = new Set<string>();
  const columns: Column[] = [];

  saved.forEach((preference) => {
    if (!isPreferenceRecord(preference) || typeof preference.name !== 'string' || applied.has(preference.name)) {
      return;
    }

    const column = byName.get(preference.name);
    if (!column) {
      return;
    }

    applied.add(column.name);
    if (column.isModifiable === false || typeof preference.isVisible !== 'boolean' || preference.isVisible === column.isVisible) {
      columns.push(column);
      return;
    }

    // Keep current labels and any getters, which can reflect updated translations.
    const copy = Object.defineProperties({} as Column, Object.getOwnPropertyDescriptors(column));
    copy.isVisible = preference.isVisible;
    columns.push(copy);
  });

  defaults.forEach((column, index) => {
    if (!applied.has(column.name)) {
      columns.splice(index, 0, column);
    }
  });

  return columns;
}
