export default function generateUUIDv4(): string {
  // getRandomValues also works on the local HTTP origins Bookshelf supports.
  // Keep the UUID v4 version/variant bits without assuming randomUUID exists.
  return '10000000-1000-4000-8000-100000000000'.replace(/[018]/g, (character) => {
    const digit = Number(character);
    // eslint-disable-next-line no-bitwise
    return (digit ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> digit / 4).toString(16);
  });
}
