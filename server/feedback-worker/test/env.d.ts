import type { D1Migration } from 'cloudflare:test';

declare global {
  namespace Cloudflare {
    interface Env {
      DB: D1Database;
      IP_HASH_SALT: string;
      TEST_MIGRATIONS: D1Migration[];
    }
  }
}
