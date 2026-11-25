import type { CheckInputProps } from 'Components/Form/CheckInput';
import type { FormInputGroupProps } from 'Components/Form/FormInputGroup';
import type { NumberInputProps } from 'Components/Form/NumberInput';
import type { SelectInputProps } from 'Components/Form/SelectInput';
import type { TextInputProps } from 'Components/Form/TextInput';
import type { SettingDraft } from 'Store/Selectors/selectSettings';

export const check: CheckInputProps = { name: 'monitor', value: null, onChange: ({ value }) => value };
export const number: NumberInputProps = { name: 'limit', value: 0, onChange: ({ value }) => value };
export const text: TextInputProps = { name: 'filename', onChange: ({ value }) => value };
export const select: SelectInputProps = { name: 'profile', value: 0, values: [{ key: 0, value: 'Default' }], onChange: ({ value }) => value };
export const group: FormInputGroupProps = { type: 'number', name: 'limit', value: 0, onChange: ({ value }) => value };

// @ts-expect-error NumberInput consumes numeric/null drafts, not display strings.
export const invalidNumber: NumberInputProps = { name: 'limit', value: '0', onChange: () => {} };
// @ts-expect-error Checkbox callbacks emit booleans, not strings.
export const invalidCheck: CheckInputProps = { name: 'monitor', onChange: (event: { value: string }) => event };
// @ts-expect-error Native select does not coerce numeric option keys into numeric DOM events.
export const invalidSelect: SelectInputProps = { name: 'profile', values: [], onChange: (event: { value: number }) => event };
// @ts-expect-error Native form groups preserve the number field's value contract.
export const invalidGroup: FormInputGroupProps = { type: 'number', name: 'limit', value: '0', onChange: () => {} };

// @ts-expect-error Provider-defined settings are not assumed to be strings until narrowed.
export const unsafeValue = (setting: SettingDraft): string => setting.value;
