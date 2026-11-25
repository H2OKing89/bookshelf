export default function padNumber(input: string | number | null | undefined, width: number, paddingCharacter: string | number = 0): string {
  if (input == null) {
    return '';
  }
  const value = String(input);
  return value.length >= width ? value : new Array(width - value.length + 1).join(String(paddingCharacter)) + value;
}
