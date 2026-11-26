import roundNumber from 'Utilities/Number/roundNumber';

export const MINIMUM_SIZE = 0;
export const MAXIMUM_SIZE = 1500;
export const SIZE_DISTANCE = 1;

export const SLIDER_MIN = MINIMUM_SIZE;
export const SLIDER_MAX = roundNumber(Math.pow(MAXIMUM_SIZE, 1 / 1.1));
export const SLIDER_STEP = 0.1;
export const SLIDER_DISTANCE = SIZE_DISTANCE * 5;

export interface QualitySizes {
  minSize?: number | null;
  maxSize?: number | null;
}

export function normalizeSize(value: number | null): number {
  const size = value != null && Number.isFinite(value) ? value : 0;
  return roundNumber(Math.max(MINIMUM_SIZE, Math.min(MAXIMUM_SIZE, size)));
}

function sliderValue(value: number | null | undefined, fallback: number): number {
  return value && Number.isFinite(value) ?
    Math.min(SLIDER_MAX, roundNumber(Math.pow(normalizeSize(value), 1 / 1.1))) : fallback;
}

export function sliderValues({ minSize, maxSize }: QualitySizes): [number, number] {
  const min = sliderValue(minSize, SLIDER_MIN);
  const max = sliderValue(maxSize, SLIDER_MAX);
  return [Math.min(min, max), max];
}

// Numeric inputs historically permit a narrower range than the slider's
// minimum visual spacing. Don't silently modify those persisted values.
export function sliderStepsBetween([min, max]: [number, number]): number {
  return Math.min(Math.round(SLIDER_DISTANCE / SLIDER_STEP), Math.round((max - min) / SLIDER_STEP));
}

export function sliderThumbBounds(values: [number, number]): [[number, number], [number, number]] {
  const [min, max] = values;
  const distance = sliderStepsBetween(values) * SLIDER_STEP;
  return [[SLIDER_MIN, roundNumber(max - distance)], [roundNumber(min + distance), SLIDER_MAX]];
}

export function sizesFromSlider(values: readonly number[], previous: QualitySizes): QualitySizes | null {
  if (values.length !== 2) {
    return null;
  }
  const [min, max] = values;
  if (!Number.isFinite(min) || !Number.isFinite(max) || min < SLIDER_MIN || max > SLIDER_MAX || min > max) {
    return null;
  }
  const [previousMin, previousMax] = sliderValues(previous);

  const nextMaximum = max === SLIDER_MAX ? null : roundNumber(Math.pow(max, 1.1));
  return {
    minSize: min === previousMin ? previous.minSize : roundNumber(Math.pow(min, 1.1)),
    maxSize: max === previousMax ? previous.maxSize : nextMaximum
  };
}
