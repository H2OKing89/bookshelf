function stripHtml<T extends string | null | undefined>(html: T): string | T {
  if (!html) {
    return html;
  }

  const fiddled = html.replace(/<br\/>/g, ' ');

  const doc = new DOMParser().parseFromString(fiddled, 'text/html');
  const text = doc.body.textContent || '';
  return text.replace(/([;,.])([^\s.])/g, '$1 $2').replace(/\s{2,}/g, ' ').replace(/s+…/g, '…');
}

export default stripHtml;
