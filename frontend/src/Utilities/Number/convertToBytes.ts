export default function convertToBytes(input: unknown, power: number, binaryPrefix: boolean): number | '' {
  const size = Number(input);
  if (Number.isNaN(size)) {
    return '';
  }
  const prefix = binaryPrefix ? 1024 : 1000;
  return Math.round(size * Math.pow(prefix, power));
}
