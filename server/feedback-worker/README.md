# TidyBox feedback Worker

A Cloudflare Worker with a D1 database that receives the feedback sent from TidyBox' Settings page.
`POST /v1/feedback` — the contract and limits are in `docs/superpowers/specs/2026-10-28-feedback-design.md`.

Run everything here from `server/feedback-worker/`.

## Develop and test locally (no Cloudflare account needed)

```powershell
npm install
npm test                      # validation + handler tests against a real (Miniflare) D1
npm run typecheck

# a local server for trying the app against it:
"IP_HASH_SALT=local-dev-salt" | Out-File -Encoding ascii .dev.vars     # git-ignored
npx wrangler d1 migrations apply feedback --local
npx wrangler dev --local --port 8787
npm run list:local            # what is in the local database
```

A Debug build of TidyBox talks to it when `TIDYBOX_FEEDBACK_ENDPOINT=http://127.0.0.1:8787/v1/feedback`
is set in the environment that starts the app.

## Deploy (needs your Cloudflare account)

1. `npx wrangler login`
2. `npx wrangler d1 create feedback` — put the printed `database_id` into `wrangler.jsonc`.
3. `npx wrangler d1 migrations apply feedback --remote`
4. `npx wrangler secret put IP_HASH_SALT` — enter a long random string. It is mixed into the IP hash; keep it, and
   do not change it casually (the per-IP rate limit forgets everyone when it changes).
5. `npx wrangler deploy`
6. Add a **custom domain** to the Worker (Cloudflare dashboard → the Worker → Settings → Domains & Routes), for example
   `feedback.<your-domain>`. Do not rely on `*.workers.dev`: it is often unreachable from mainland China.
7. Smoke test, then delete the test row:
   ```powershell
   curl.exe -i -X POST https://feedback.<your-domain>/v1/feedback -H "Content-Type: application/json" -d '{\"kind\":\"other\",\"message\":\"smoke test\",\"appVersion\":\"1.0.0.0\"}'
   npx wrangler d1 execute feedback --remote --command "DELETE FROM feedback WHERE message = 'smoke test'"
   ```
8. Set `FeedbackConfig.Endpoint` in `src/TidyBox.App/Feedback/FeedbackConfig.cs` to
   `https://feedback.<your-domain>/v1/feedback` and rebuild. While it is empty the feedback entry is hidden.

## Read the feedback

```powershell
npm run list                  # the latest 50: id, time, kind, first 80 characters, contact, version, log size
npx wrangler d1 execute feedback --remote --command "SELECT * FROM feedback WHERE id = 42"   # one in full, with its logs
```

Delete one (for example when a user asks): `npx wrangler d1 execute feedback --remote --command "DELETE FROM feedback WHERE id = 42"`.

## Limits worth knowing

Per IP hash: 5 feedbacks per hour. Whole site: 2000 per day. The endpoint is public (a desktop app cannot keep a
secret), so these limits are what stands between it and a script.

## Storage and cost

Rows are never deleted automatically. Within the limits one accepted feedback is at most about 70 KB (4000 characters of
text plus 64 KB of log), and the site-wide cap is 2000 a day, so the worst case is on the order of 100+ MB a day if the
endpoint is being abused. Cloudflare's free D1 plan has a database size limit and a rows-read quota (check the current
numbers in the Cloudflare docs); when the database is full inserts fail and the app tells the user it cannot connect.

- Check the size now and then: `npx wrangler d1 info feedback`.
- Clean up old rows: `npx wrangler d1 execute feedback --remote --command "DELETE FROM feedback WHERE created_at < '2027-01-01'"`.
- If you see abuse, lower `RATE.globalPerDay` in `src/handler.ts`, or add a Cloudflare WAF rate-limiting rule in front of
  the Worker.
