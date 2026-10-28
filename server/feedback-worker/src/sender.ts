const MAPPED_IPV4 = /^::ffff:(\d{1,3}(?:\.\d{1,3}){3})$/i;

/**
 * What the per-sender limit is keyed on. An IPv4 address is itself. An IPv6 address is reduced to its /64 (the first four
 * groups): a single home or VPS connection is handed a whole /64, so keying on the full address would let one machine
 * rotate through as many "senders" as it likes.
 */
export function senderKey(address: string): string {
  const mapped = MAPPED_IPV4.exec(address);
  if (mapped) return mapped[1];
  if (!address.includes(':')) return address;

  const [head, tail] = address.toLowerCase().split('::');
  const first = head === '' ? [] : head.split(':');
  const last = tail === undefined || tail === '' ? [] : tail.split(':');
  const groups = tail === undefined ? first : [...first, ...Array<string>(Math.max(0, 8 - first.length - last.length)).fill('0'), ...last];

  return groups
    .slice(0, 4)
    .map((group) => group.replace(/^0+(?=.)/, ''))
    .join(':');
}
