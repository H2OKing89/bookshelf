export function textValue(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

export function numberValue(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) ? value : null;
}

export function checkValue(value: unknown): boolean | string | null {
  return typeof value === 'boolean' || typeof value === 'string' ? value : null;
}
