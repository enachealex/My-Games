# Graveway's online scoreboard

The boards the game reads and sends runs to: the best runs in the world, and in each country.
It runs on the home server (see `/srv/apps/README.md` there, mirrored at
`Documents/GitHub/HOMELAB-SERVER.md`, which has the full deployment notes) in the same shape as
Roadbook: no app process of its own, only data in Supabase and a path-restricted Cloudflare tunnel.

| | |
|---|---|
| Address | `https://graveway-api.thejumpvault.com` |
| Data | schema `graveway` (table `scores`) in Supabase's `postgres` database, covered by its daily, restore-verified backup |
| API | `public.graveway_submit_score` and `public.graveway_scores`, PostgREST RPC through Kong |
| Tunnel | `graveway` (`graveway.yml` here is a copy of `~/.cloudflared/graveway.yml`), pm2 `graveway-tunnel` |
| Key | Supabase's anon key: the only one Kong takes for the API |

## How it works

- **A run** goes up with the id the phone gave it (`Score.id`), its name, survivor, metres, kills,
  seconds and furthest place. The server checks it's a run someone could have played, and that the
  address hasn't sent 60 in the last hour. The same id again can only change the name (renamed on
  the results after it went).
- **The region** is Cloudflare's: its `CF-IPCountry` header, read in SQL from PostgREST's
  `request.headers`. The phone never says where it is, and can't claim another country.
- **The boards** come back best first with each run's place, and the first 12 characters of
  `md5(id)` (never the id), so the phone can pick out its own runs without anyone else learning
  an id they could rename.
- **The game** (`Scripts/Core/Online.cs`) keeps runs waiting in the save until they're sent, so a
  run played offline goes the next time there's a connection. In the editor, and so in every test,
  runs are only sent as dry runs: checked and ranked, never kept.

## Keys and the build

The address and key live outside the repo, at `%USERPROFILE%\.graveway\online.json`:

```json
{ "url": "https://graveway-api.thejumpvault.com", "key": "<the anon key>" }
```

*Graveway → Set up project* and every build copy it to `Assets/Graveway/Resources/Online.json`,
which git ignores. A build without it is offline and says so in the build log. To make the file
again without printing the key:

```bash
ssh -i ~/.ssh/id_ed25519 huckleberry@100.112.51.64 "grep '^ANON_KEY=' /srv/apps/supabase/.env | cut -d= -f2-" \
  | python -c "import sys,json,os; json.dump({'url':'https://graveway-api.thejumpvault.com','key':sys.stdin.read().strip()}, open(os.path.expanduser('~/.graveway/online.json'),'w'))"
```

## Changing the schema

`scoreboard.sql` is safe to run again. `scoreboard-test.sql` checks it in place as the API's own
role, in a transaction it rolls back, so it keeps nothing:

```bash
scp -i ~/.ssh/id_ed25519 scoreboard.sql scoreboard-test.sql huckleberry@100.112.51.64:/tmp/
ssh -i ~/.ssh/id_ed25519 huckleberry@100.112.51.64 "docker exec -i supabase-db psql -U postgres -d postgres -v ON_ERROR_STOP=1 --single-transaction < /tmp/scoreboard.sql && docker exec -i supabase-db psql -U postgres -d postgres -v ON_ERROR_STOP=1 < /tmp/scoreboard-test.sql"
```

A new API function also needs adding to the tunnel's path list (`~/.cloudflared/graveway.yml`,
then `cloudflared tunnel --config ~/.cloudflared/graveway.yml ingress validate` and
`pm2 restart graveway-tunnel`), and to the copy here.

## Testing it by hand

Cloudflare's Browser Integrity Check turns away an empty user agent or Python's default with
**error 1010**; send a real one, as the game does (`Graveway/<build> (Unity)`):

```bash
curl -s -A "Graveway/0 (test)" -H "apikey: $KEY" -H "Content-Type: application/json" \
  -d '{"p_scope":"world","p_limit":5}' https://graveway-api.thejumpvault.com/rest/v1/rpc/graveway_scores
```

Any other path on that address is a 404 from the tunnel, and a call without the key a 401.

## Removing it

`pm2 delete graveway-tunnel && pm2 save`, `cloudflared tunnel delete graveway`, delete the
`graveway-api` DNS record in Cloudflare, then the `drop` statements at the top of `scoreboard.sql`.
