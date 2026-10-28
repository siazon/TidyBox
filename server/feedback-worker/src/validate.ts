export const LIMITS = {
  maxBodyBytes: 131072,
  maxMessage: 4000,
  maxContact: 200,
  maxLogBytes: 65536,
} as const;

export type FeedbackKind = 'suggestion' | 'error' | 'other';

export interface FeedbackInput {
  kind: FeedbackKind;
  message: string;
  contact: string | null;
  appVersion: string;
  logs: string | null;
}

export type ValidationResult =
  | { ok: true; value: FeedbackInput }
  | { ok: false; status: 400 | 413; error: 'invalid_request' | 'too_large'; field: string };

const KINDS: readonly string[] = ['suggestion', 'error', 'other'];
const VERSION = /^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$/;
const encoder = new TextEncoder();

const invalid = (field: string): ValidationResult => ({ ok: false, status: 400, error: 'invalid_request', field });

/** Checks a parsed request body against the contract and returns the cleaned-up input. */
export function validateFeedback(body: unknown): ValidationResult {
  if (typeof body !== 'object' || body === null || Array.isArray(body)) return invalid('body');
  const input = body as Record<string, unknown>;

  if (typeof input.kind !== 'string' || !KINDS.includes(input.kind)) return invalid('kind');
  const kind = input.kind as FeedbackKind;

  if (typeof input.message !== 'string') return invalid('message');
  const message = input.message.trim();
  if (message.length === 0 || message.length > LIMITS.maxMessage) return invalid('message');

  let contact: string | null = null;
  if (input.contact !== undefined && input.contact !== null) {
    if (typeof input.contact !== 'string') return invalid('contact');
    const trimmed = input.contact.trim();
    if (trimmed.length > LIMITS.maxContact) return invalid('contact');
    contact = trimmed.length === 0 ? null : trimmed;
  }

  if (typeof input.appVersion !== 'string' || !VERSION.test(input.appVersion)) return invalid('appVersion');

  let logs: string | null = null;
  if (input.logs !== undefined && input.logs !== null) {
    if (typeof input.logs !== 'string' || kind !== 'error') return invalid('logs');
    if (encoder.encode(input.logs).length > LIMITS.maxLogBytes) {
      return { ok: false, status: 413, error: 'too_large', field: 'logs' };
    }
    logs = input.logs;
  }

  return { ok: true, value: { kind, message, contact, appVersion: input.appVersion, logs } };
}
