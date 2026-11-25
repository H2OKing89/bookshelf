export default function getErrorMessage(xhr: unknown, fallbackErrorMessage: string): string {
  if (typeof xhr === 'object' && xhr !== null && 'responseJSON' in xhr) {
    const body = xhr.responseJSON;
    if (typeof body === 'object' && body !== null && 'message' in body && typeof body.message === 'string' && body.message.length > 0) {
      return body.message;
    }
  }
  return fallbackErrorMessage;
}
