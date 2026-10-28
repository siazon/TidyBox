import { describe, expect, it } from 'vitest';
import { validateFeedback } from '../src/validate';

const valid = { kind: 'suggestion', message: 'hi', appVersion: '1.2.3.4' };

function failure(body: unknown) {
  const result = validateFeedback(body);
  if (result.ok) throw new Error('expected a failure, got ' + JSON.stringify(result.value));
  return result;
}

describe('validateFeedback', () => {
  it('accepts the minimal input and normalises it', () => {
    const result = validateFeedback({ kind: 'suggestion', message: '  hi  ', contact: '', appVersion: '1.2.3.4' });

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.value).toEqual({ kind: 'suggestion', message: 'hi', contact: null, appVersion: '1.2.3.4', logs: null });
  });

  describe('message', () => {
    it('allows exactly 4000 characters and rejects 4001', () => {
      expect(validateFeedback({ ...valid, message: 'a'.repeat(4000) }).ok).toBe(true);
      expect(failure({ ...valid, message: 'a'.repeat(4001) })).toMatchObject({ status: 400, field: 'message' });
    });

    it('rejects whitespace only and missing', () => {
      expect(failure({ ...valid, message: ' \n\t ' })).toMatchObject({ status: 400, field: 'message' });
      expect(failure({ kind: 'suggestion', appVersion: '1.2.3.4' })).toMatchObject({ status: 400, field: 'message' });
    });

    it('counts UTF-16 units, so emoji count double', () => {
      expect(validateFeedback({ ...valid, message: '😀'.repeat(2000) }).ok).toBe(true); // length 4000
      expect(failure({ ...valid, message: '😀'.repeat(2001) })).toMatchObject({ status: 400, field: 'message' });
    });

    it('rejects a non-string', () => {
      expect(failure({ ...valid, message: 42 })).toMatchObject({ status: 400, field: 'message' });
    });
  });

  describe('contact', () => {
    it('allows 200 characters, rejects 201, and trims', () => {
      expect(validateFeedback({ ...valid, contact: 'c'.repeat(200) }).ok).toBe(true);
      expect(failure({ ...valid, contact: 'c'.repeat(201) })).toMatchObject({ status: 400, field: 'contact' });

      const trimmed = validateFeedback({ ...valid, contact: '  qq123  ' });
      expect(trimmed.ok && trimmed.value.contact).toBe('qq123');
    });
  });

  describe('kind', () => {
    it('rejects a missing or unknown kind', () => {
      expect(failure({ message: 'hi', appVersion: '1.2.3.4' })).toMatchObject({ status: 400, field: 'kind' });
      expect(failure({ ...valid, kind: 'bug' })).toMatchObject({ status: 400, field: 'kind' });
    });
  });

  describe('appVersion', () => {
    it('accepts four numeric parts only', () => {
      expect(validateFeedback({ ...valid, appVersion: '1.2.3.4' }).ok).toBe(true);
      for (const bad of ['1.2.3', '1.2.3.4.5', 'v1.2.3.4', '1.2.3.123456', '']) {
        expect(failure({ ...valid, appVersion: bad })).toMatchObject({ status: 400, field: 'appVersion' });
      }
    });
  });

  describe('logs', () => {
    it('are accepted with kind "error"', () => {
      const result = validateFeedback({ ...valid, kind: 'error', logs: 'abc' });
      expect(result.ok && result.value.logs).toBe('abc');
    });

    it('are rejected for any other kind', () => {
      expect(failure({ ...valid, kind: 'suggestion', logs: 'abc' })).toMatchObject({ status: 400, field: 'logs' });
      expect(failure({ ...valid, kind: 'other', logs: 'abc' })).toMatchObject({ status: 400, field: 'logs' });
    });

    it('allow 65536 bytes and reject 65537 with 413', () => {
      expect(validateFeedback({ ...valid, kind: 'error', logs: 'a'.repeat(65536) }).ok).toBe(true);
      expect(failure({ ...valid, kind: 'error', logs: 'a'.repeat(65537) })).toMatchObject({ status: 413, error: 'too_large', field: 'logs' });
    });

    it('are measured in UTF-8 bytes, not characters', () => {
      expect(validateFeedback({ ...valid, kind: 'error', logs: '中'.repeat(21845) }).ok).toBe(true); // 65535 bytes
      expect(failure({ ...valid, kind: 'error', logs: '中'.repeat(21846) })).toMatchObject({ status: 413, field: 'logs' }); // 65538 bytes
    });
  });

  it('rejects a body that is not an object', () => {
    for (const bad of [null, [], 'text', 42]) {
      expect(failure(bad)).toMatchObject({ status: 400, error: 'invalid_request', field: 'body' });
    }
  });
});
