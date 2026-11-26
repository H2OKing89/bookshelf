import type { SettingsProjection } from 'Store/Selectors/selectSettings';
import { asStateRecord } from 'Store/StateTypes';
import { checkValue, numberValue, textValue } from '../../../Components/Form/formValues';
import { readDraft } from '../../settingDrafts';

export type NamingField = 'standardBookFormat' | 'authorFolderFormat';
export type TokenCase = 'title' | 'lower' | 'upper';

export interface NamingExamples {
  singleBookExample?: string;
  multiPartBookExample?: string;
  authorFolderExample?: string;
}

export function namingDrafts(settings: SettingsProjection) {
  return {
    renameBooks: readDraft(settings, 'renameBooks', checkValue),
    replaceIllegalCharacters: readDraft(settings, 'replaceIllegalCharacters', checkValue),
    colonReplacementFormat: readDraft(settings, 'colonReplacementFormat', numberValue),
    standardBookFormat: readDraft(settings, 'standardBookFormat', textValue),
    authorFolderFormat: readDraft(settings, 'authorFolderFormat', textValue)
  };
}

export function namingExamples(value: unknown): NamingExamples {
  const examples = asStateRecord(value);
  return {
    singleBookExample: typeof examples.singleBookExample === 'string' ? examples.singleBookExample : undefined,
    multiPartBookExample: typeof examples.multiPartBookExample === 'string' ? examples.multiPartBookExample : undefined,
    authorFolderExample: typeof examples.authorFolderExample === 'string' ? examples.authorFolderExample : undefined
  };
}

export function formatToken(token: string, separator: string, tokenCase: TokenCase): string {
  const formatted = token.replace(/ /g, separator);
  if (tokenCase === 'lower') {
    return formatted.toLowerCase();
  }
  if (tokenCase === 'upper') {
    return formatted.toUpperCase();
  }
  return formatted;
}

export function insertNamingToken(
  value: string | null | undefined,
  token: string,
  selectionStart: number | null,
  selectionEnd: number | null,
  isFullFilename: boolean
) {
  const text = value ?? '';
  const start = isFullFilename ? 0 : Math.max(0, Math.min(text.length, selectionStart ?? text.length));
  const end = isFullFilename ? text.length : Math.max(start, Math.min(text.length, selectionEnd ?? start));
  const result = text.substring(0, start) + token + text.substring(end);
  return { value: result, cursor: start + token.length };
}
