import { senderKey } from './sender';
import { LIMITS, validateFeedback } from './validate';

export interface Env {
  DB: D1Database;
  /** Secret mixed into the IP hash, so the stored hashes cannot be reversed by trying every address. */
  IP_HASH_SALT: string;
}

export const RATE = { perIpPerHour: 5, globalPerDay: 2000 } as const;

const json = (status: number, body: unknown, headers?: Record<string, string>) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json', ...headers } });

async function sha256Hex(text: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
  return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('');
}

/** Reads the body up to `max` bytes; null when it is larger (without reading the rest). */
async function readLimited(request: Request, max: number): Promise<string | null> {
  const declared = Number(request.headers.get('content-length'));
  if (Number.isFinite(declared) && declared > max) return null;
  if (!request.body) return '';

  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  for (;;) {
    const { done, value } = await reader.read();
    if (done) break;
    total += value.byteLength;
    if (total > max) {
      await reader.cancel();
      return null;
    }
    chunks.push(value);
  }

  const all = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    all.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return new TextDecoder().decode(all);
}

export async function handleRequest(request: Request, env: Env, now: Date = new Date()): Promise<Response> {
  if (new URL(request.url).pathname !== '/v1/feedback') return json(404, { error: 'not_found' });
  if (request.method !== 'POST') return json(405, { error: 'method_not_allowed' }, { allow: 'POST' });

  // A web page can POST text/plain to any address without the browser asking first (no CORS preflight), so a missing or
  // other content type is how a page using its visitors' browsers would get in. The real client always sends JSON.
  if (!(request.headers.get('content-type') ?? '').toLowerCase().startsWith('application/json')) {
    return json(415, { error: 'unsupported_media_type' });
  }

  const text = await readLimited(request, LIMITS.maxBodyBytes);
  if (text === null) return json(413, { error: 'too_large' });

  let body: unknown;
  try {
    body = JSON.parse(text);
  } catch {
    return json(400, { error: 'invalid_request', field: 'body' });
  }

  const result = validateFeedback(body);
  if (!result.ok) {
    return json(result.status, result.status === 413 ? { error: result.error } : { error: result.error, field: result.field });
  }

  const ipHash = await sha256Hex(env.IP_HASH_SALT + senderKey(request.headers.get('CF-Connecting-IP') ?? 'unknown'));
  const hourAgo = new Date(now.getTime() - 60 * 60 * 1000).toISOString();
  const dayAgo = new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString();

  try {
    // Checking the limits and inserting are ONE statement: separate "count, then insert" steps let requests that arrive
    // together all see room and all get in. SQLite runs a single statement as a unit.
    const { kind, message, contact, appVersion, logs } = result.value;
    const run = await env.DB.prepare(
      `INSERT INTO feedback (created_at, kind, message, contact, app_version, logs, ip_hash)
       SELECT ?1, ?2, ?3, ?4, ?5, ?6, ?7
       WHERE (SELECT COUNT(*) FROM feedback WHERE ip_hash = ?7 AND created_at > ?8) < ?9
         AND (SELECT COUNT(*) FROM feedback WHERE created_at > ?10) < ?11`,
    )
      .bind(now.toISOString(), kind, message, contact, appVersion, logs, ipHash, hourAgo, RATE.perIpPerHour, dayAgo, RATE.globalPerDay)
      .run();
    if (run.meta.changes > 0) return json(201, { id: run.meta.last_row_id });

    // Nothing was inserted, so a limit was reached; find out which (the per-sender one is reported first).
    const fromThisIp = await env.DB.prepare('SELECT COUNT(*) AS n FROM feedback WHERE ip_hash = ?1 AND created_at > ?2')
      .bind(ipHash, hourAgo)
      .first<{ n: number }>();
    return (fromThisIp?.n ?? 0) >= RATE.perIpPerHour ? json(429, { error: 'rate_limited' }) : json(503, { error: 'busy' });
  } catch {
    return json(500, { error: 'server_error' });
  }
}
