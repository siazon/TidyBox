import { SELF, env } from 'cloudflare:test';
import { beforeEach, describe, expect, it } from 'vitest';
import { handleRequest, RATE } from '../src/handler';

const NOW = new Date('2026-10-28T12:00:00.000Z');
const valid = { kind: 'suggestion', message: 'hello', contact: 'a@b.c', appVersion: '1.2.3.4' };

function post(body: unknown, options: { ip?: string | null; raw?: string; path?: string; method?: string; headers?: Record<string, string> } = {}) {
  const headers = new Headers({ 'content-type': 'application/json', ...options.headers });
  if (options.ip !== null) headers.set('CF-Connecting-IP', options.ip ?? '203.0.113.7');
  const method = options.method ?? 'POST';
  return new Request('https://feedback.test' + (options.path ?? '/v1/feedback'), {
    method,
    headers,
    body: method === 'GET' ? undefined : (options.raw ?? JSON.stringify(body)),
  });
}

async function count(): Promise<number> {
  const row = await env.DB.prepare('SELECT COUNT(*) AS n FROM feedback').first<{ n: number }>();
  return row!.n;
}

async function insertMany(rows: number, createdAt: string) {
  await env.DB.prepare(
    `WITH RECURSIVE seq(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM seq WHERE i < ?1)
     INSERT INTO feedback (created_at, kind, message, app_version, ip_hash)
     SELECT ?2, 'other', 'x', '1.0.0.0', 'ip' || i FROM seq`,
  )
    .bind(rows, createdAt)
    .run();
}

beforeEach(async () => {
  await env.DB.prepare('DELETE FROM feedback').run();
});

describe('storing', () => {
  it('saves a valid feedback and answers 201 with the new id', async () => {
    const response = await handleRequest(post(valid), env, NOW);

    expect(response.status).toBe(201);
    expect(response.headers.get('content-type')).toContain('application/json');
    const { id } = (await response.json()) as { id: number };
    const row = await env.DB.prepare('SELECT * FROM feedback WHERE id = ?1').bind(id).first<Record<string, unknown>>();
    expect(row).toMatchObject({
      created_at: NOW.toISOString(),
      kind: 'suggestion',
      message: 'hello',
      contact: 'a@b.c',
      app_version: '1.2.3.4',
      logs: null,
    });
  });

  it('keeps only a hash of the IP address', async () => {
    await handleRequest(post(valid, { ip: '203.0.113.7' }), env, NOW);

    const row = await env.DB.prepare('SELECT * FROM feedback').first<Record<string, unknown>>();
    expect(row!.ip_hash).toMatch(/^[0-9a-f]{64}$/);
    expect(JSON.stringify(row)).not.toContain('203.0.113.7');
  });

  it('stores the logs of an error report', async () => {
    const response = await handleRequest(post({ ...valid, kind: 'error', logs: 'line 1\nline 2' }), env, NOW);

    expect(response.status).toBe(201);
    const row = await env.DB.prepare('SELECT logs FROM feedback').first<{ logs: string }>();
    expect(row!.logs).toBe('line 1\nline 2');
  });

  it('works without a CF-Connecting-IP header', async () => {
    const response = await handleRequest(post(valid, { ip: null }), env, NOW);

    expect(response.status).toBe(201);
  });
});

describe('routing', () => {
  it('answers 405 to anything but POST', async () => {
    const response = await handleRequest(post(undefined, { method: 'GET' }), env, NOW);

    expect(response.status).toBe(405);
    expect(await response.json()).toEqual({ error: 'method_not_allowed' });
    expect(response.headers.get('content-type')).toContain('application/json');
  });

  it('answers 404 to another path', async () => {
    const response = await handleRequest(post(valid, { path: '/nope' }), env, NOW);

    expect(response.status).toBe(404);
    expect(await response.json()).toEqual({ error: 'not_found' });
  });

  it('is wired up as the Worker entry point', async () => {
    const response = await SELF.fetch('https://feedback.test/v1/feedback', {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(valid),
    });

    expect(response.status).toBe(201);
  });
});

describe('rejecting', () => {
  it('answers 415 to a body that is not declared as JSON and stores nothing', async () => {
    // A web page can send text/plain across origins without a preflight; only the real client sends JSON.
    for (const type of ['text/plain', 'application/x-www-form-urlencoded', '']) {
      const response = await handleRequest(post(valid, { headers: { 'content-type': type } }), env, NOW);

      expect(response.status).toBe(415);
      expect(await response.json()).toEqual({ error: 'unsupported_media_type' });
    }
    expect(await count()).toBe(0);
  });

  it('accepts a JSON content type with parameters', async () => {
    const response = await handleRequest(post(valid, { headers: { 'content-type': 'application/json; charset=utf-8' } }), env, NOW);

    expect(response.status).toBe(201);
  });

  it('answers 400 to a broken JSON body and stores nothing', async () => {
    const response = await handleRequest(post(undefined, { raw: '{ not json' }), env, NOW);

    expect(response.status).toBe(400);
    expect(await response.json()).toEqual({ error: 'invalid_request', field: 'body' });
    expect(await count()).toBe(0);
  });

  it('answers 400 to an invalid field, naming it, and stores nothing', async () => {
    const response = await handleRequest(post({ ...valid, message: '   ' }), env, NOW);

    expect(response.status).toBe(400);
    expect(await response.json()).toEqual({ error: 'invalid_request', field: 'message' });
    expect(await count()).toBe(0);
  });

  it('answers 413 to a body over 128 KB and stores nothing', async () => {
    const response = await handleRequest(post(undefined, { raw: 'x'.repeat(131073) }), env, NOW);

    expect(response.status).toBe(413);
    expect(await response.json()).toEqual({ error: 'too_large' });
    expect(await count()).toBe(0);
  });

  it('answers 413 from the declared length alone, without reading the body', async () => {
    const response = await handleRequest(post(valid, { headers: { 'content-length': '999999' } }), env, NOW);

    expect(response.status).toBe(413);
  });

  it('answers 413 to logs over 64 KB with a plain too_large body', async () => {
    const response = await handleRequest(post({ ...valid, kind: 'error', logs: 'a'.repeat(65537) }), env, NOW);

    expect(response.status).toBe(413);
    expect(await response.json()).toEqual({ error: 'too_large' });
    expect(await count()).toBe(0);
  });
});

describe('rate limits', () => {
  it('lets one IP send 5 per hour, then answers 429', async () => {
    for (let i = 0; i < RATE.perIpPerHour; i++) {
      expect((await handleRequest(post(valid), env, NOW)).status).toBe(201);
    }

    const sixth = await handleRequest(post(valid), env, NOW);

    expect(sixth.status).toBe(429);
    expect(await sixth.json()).toEqual({ error: 'rate_limited' });
    expect(await count()).toBe(5);
  });

  it('does not hold one IP against another', async () => {
    for (let i = 0; i < RATE.perIpPerHour; i++) await handleRequest(post(valid, { ip: '203.0.113.7' }), env, NOW);

    expect((await handleRequest(post(valid, { ip: '203.0.113.8' }), env, NOW)).status).toBe(201);
  });

  it('lets the same IP send again after an hour', async () => {
    for (let i = 0; i < RATE.perIpPerHour; i++) await handleRequest(post(valid), env, NOW);

    const later = new Date(NOW.getTime() + 61 * 60 * 1000);

    expect((await handleRequest(post(valid), env, later)).status).toBe(201);
  });

  it('holds the limit when requests arrive at the same moment', async () => {
    const answers = await Promise.all(Array.from({ length: 12 }, () => handleRequest(post(valid), env, NOW)));

    const statuses = answers.map((a) => a.status).sort();
    expect(statuses.filter((s) => s === 201)).toHaveLength(RATE.perIpPerHour);
    expect(statuses.filter((s) => s === 429)).toHaveLength(12 - RATE.perIpPerHour);
    expect(await count()).toBe(RATE.perIpPerHour);
  });

  it('counts every address of one IPv6 /64 as the same sender', async () => {
    const inside = ['2001:db8:1:2::1', '2001:db8:1:2:aaaa:bbbb:cccc:dddd', '2001:db8:1:2::ffff'];
    for (let i = 0; i < RATE.perIpPerHour; i++) {
      expect((await handleRequest(post(valid, { ip: inside[i % inside.length] }), env, NOW)).status).toBe(201);
    }

    expect((await handleRequest(post(valid, { ip: '2001:db8:1:2:1:2:3:4' }), env, NOW)).status).toBe(429);
    expect((await handleRequest(post(valid, { ip: '2001:db8:1:3::1' }), env, NOW)).status).toBe(201); // another /64
  });

  it('answers 503 once the whole site has taken 2000 in a day', async () => {
    await insertMany(RATE.globalPerDay, new Date(NOW.getTime() - 3600_000).toISOString());

    const response = await handleRequest(post(valid), env, NOW);

    expect(response.status).toBe(503);
    expect(await response.json()).toEqual({ error: 'busy' });
    expect(await count()).toBe(RATE.globalPerDay);
  });

  it('still accepts at 1999 for the day', async () => {
    await insertMany(RATE.globalPerDay - 1, new Date(NOW.getTime() - 3600_000).toISOString());

    expect((await handleRequest(post(valid), env, NOW)).status).toBe(201);
  });
});

describe('failing', () => {
  it('answers 500 without leaking what went wrong', async () => {
    const broken = {
      ...env,
      DB: {
        prepare() {
          throw new Error('boom: secret internals');
        },
      } as unknown as D1Database,
    };

    const response = await handleRequest(post(valid), broken, NOW);

    expect(response.status).toBe(500);
    expect(await response.text()).not.toContain('boom');
  });
});
