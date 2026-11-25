export default function split(input: string | null | undefined, separator: string | RegExp = ','): string[] {
  return input ? input.split(separator).filter((part) => !!part) : [];
}
