type OptionKey = string | number;

interface SelectOption {
  key: OptionKey;
  isDisabled?: boolean;
}

export function selectedOptionIndex(value: OptionKey | readonly OptionKey[], values: readonly SelectOption[]) {
  const key = Array.isArray(value) ? value[0] : value;
  return values.findIndex((option) => option.key === key);
}

export function nextEnabledOptionIndex(startingIndex: number, direction: -1 | 1, values: readonly SelectOption[]) {
  if (!values.length) {
    return -1;
  }

  let index = direction === 1 ? -1 : 0;
  if (startingIndex >= 0 && startingIndex < values.length) {
    index = startingIndex;
  }

  // Bound the scan even if the list is empty or every option is disabled.
  for (let attempt = 0; attempt < values.length; attempt++) {
    index = (index + direction + values.length) % values.length;
    if (!values[index].isDisabled) {
      return index;
    }
  }

  return -1;
}
