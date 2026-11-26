export type TagIdentity = string | number | boolean;

export interface RenderedTag<Id extends TagIdentity> {
  id: Id;
  name: string;
}

export interface TagChoice<Id extends TagIdentity> {
  id?: Id;
  name: string;
}

export interface TagSelection<Id extends TagIdentity> {
  id?: Id;
  index: number;
}

export interface TagEdit<Id extends TagIdentity> extends TagSelection<Id> {
  value: string;
}

export function resolveTag<Id extends TagIdentity>(
  value: string,
  highlighted: TagChoice<Id> | null | undefined,
  suggestions: readonly TagChoice<Id>[],
  allowNew: boolean
): TagChoice<Id> | undefined {
  if (highlighted) {
    return highlighted;
  }

  if (!value) {
    return undefined;
  }

  return suggestions.find((suggestion) => suggestion.name === value) ?? (allowNew ? { name: value } : undefined);
}
