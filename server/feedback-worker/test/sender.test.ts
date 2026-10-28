import { describe, expect, it } from 'vitest';
import { senderKey } from '../src/sender';

describe('senderKey', () => {
  it('keeps an IPv4 address as it is', () => {
    expect(senderKey('203.0.113.7')).toBe('203.0.113.7');
  });

  it('reduces an IPv6 address to its /64 (the first four groups)', () => {
    expect(senderKey('2001:db8:1:2:3:4:5:6')).toBe('2001:db8:1:2');
  });

  it('expands :: before taking the first four groups', () => {
    expect(senderKey('2001:db8::1')).toBe('2001:db8:0:0');
    expect(senderKey('::1')).toBe('0:0:0:0');
    expect(senderKey('fe80::1:2')).toBe('fe80:0:0:0');
    expect(senderKey('2001:db8:1:2::9')).toBe('2001:db8:1:2');
  });

  it('writes groups the same way however they were typed', () => {
    expect(senderKey('2001:0DB8:0000:0001::1')).toBe('2001:db8:0:1');
    expect(senderKey('2001:db8:0:1:0:0:0:1')).toBe('2001:db8:0:1');
  });

  it('treats an IPv4-mapped IPv6 address as the IPv4 address', () => {
    expect(senderKey('::ffff:203.0.113.7')).toBe('203.0.113.7');
  });

  it('leaves anything that is not an address alone', () => {
    expect(senderKey('unknown')).toBe('unknown');
  });
});
