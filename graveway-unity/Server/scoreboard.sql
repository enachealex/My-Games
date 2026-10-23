-- Graveway's online scoreboard, for Supabase on the home server (see README.md here).
--
-- Run the whole file against the Supabase "postgres" database. It is safe to run again.
--
-- Access model: anyone with the game can post a run and read the boards; there are no
-- accounts (a run carries whatever name was typed on the phone). The data lives in its own
-- "graveway" schema, which the public API cannot see at all; everything goes through the two
-- functions at the bottom, which are also the only paths the public tunnel lets through.
--
--   graveway_submit_score  a run, with the id the phone gave it. The same id again can only
--                          change the run's name (typed on the results after it was sent).
--   graveway_scores        the best runs: in the world, or in one country.
--
-- The country is Cloudflare's (its CF-IPCountry header), never the phone's: the region a run
-- counts for is where it was sent from. Runs are checked for being possible (no faster than a
-- survivor runs, no more kills than the guns allow), and one address can send at most 60 an
-- hour. That keeps out mistakes and casual meddling, not a determined cheat: anything the game
-- can send, someone can fake.
--
-- To remove it entirely:
--   drop schema graveway cascade;
--   drop function public.graveway_submit_score(uuid, text, text, integer, integer, integer, integer, integer, boolean),
--     public.graveway_scores(text, integer, text);

create schema if not exists graveway;
revoke all on schema graveway from public, anon, authenticated;

create table if not exists graveway.scores (
  id          uuid primary key,                    -- made on the phone, one per run
  name        text not null default '' check (char_length(name) <= 16),
  survivor    text not null check (survivor in ('rook', 'bram', 'juno', 'wren')),
  distance    integer not null check (distance between 1 and 100000),
  kills       integer not null check (kills between 0 and 2000000),
  seconds     integer not null check (seconds between 1 and 86400),
  place       integer not null default 0 check (place between 0 and 1000),  -- 0 downtown, 1 the docks...
  country     text not null default '' check (country ~ '^([A-Z]{2})?$'),
  build       integer not null default 0,          -- the game's version code
  sender      text not null default '',            -- md5 of the address it came from: for the rate limit only
  at          timestamptz not null default now(),
  renamed_at  timestamptz
);
create index if not exists scores_best on graveway.scores (distance desc, kills desc, at);
create index if not exists scores_country_best on graveway.scores (country, distance desc, kills desc, at);
create index if not exists scores_sender on graveway.scores (sender, at);
revoke all on graveway.scores from public, anon, authenticated;

-- ---------------------------------------------------------------------------
-- Helpers
-- ---------------------------------------------------------------------------

-- A header of the request PostgREST is answering ('' when there isn't one).
create or replace function graveway.header(p_name text)
returns text language sql stable set search_path = '' as $$
  select coalesce((nullif(current_setting('request.headers', true), '')::json) ->> p_name, '')
$$;

-- Where the request came from, by Cloudflare: two letters, or '' (unknown, Tor, or not
-- through Cloudflare at all).
create or replace function graveway.country()
returns text language sql stable set search_path = '' as $$
  select case when c ~ '^[A-Z]{2}$' and c not in ('XX', 'T1') then c else '' end
  from (select upper(graveway.header('cf-ipcountry')) as c) h
$$;

-- A name as the game cleans it: one line, single spaces, at most 16 letters.
create or replace function graveway.clean_name(p_name text)
returns text language sql immutable set search_path = '' as $$
  select btrim(left(btrim(regexp_replace(regexp_replace(coalesce(p_name, ''), '[[:cntrl:]]', ' ', 'g'), '\s+', ' ', 'g')), 16))
$$;

-- A run's place on a board: one more than the runs ahead of it (further, or as far with
-- more kills). Ties share a place.
create or replace function graveway.rank(p_distance integer, p_kills integer, p_country text)
returns integer language sql stable set search_path = '' as $$
  select 1 + count(*)::integer from graveway.scores s
  where (p_country is null or s.country = p_country)
    and (s.distance > p_distance or (s.distance = p_distance and s.kills > p_kills))
$$;

-- ---------------------------------------------------------------------------
-- The API
-- ---------------------------------------------------------------------------

-- A run onto the boards (or with p_dry_run, only checked and ranked: for tests). Returns
-- where it is: {"country": "US", "world": 12, "region": 3} (region null with no country).
create or replace function public.graveway_submit_score(
  p_id uuid, p_name text, p_survivor text, p_distance integer, p_kills integer, p_seconds integer,
  p_place integer default 0, p_build integer default 0, p_dry_run boolean default false)
returns jsonb language plpgsql volatile security definer set search_path = '' as $$
declare
  v_name text := graveway.clean_name(p_name);
  v_country text := graveway.country();
  v_sender text := md5(graveway.header('cf-connecting-ip') || ':graveway');
  v_run graveway.scores;
begin
  if p_id is null or p_survivor is null or p_survivor not in ('rook', 'bram', 'juno', 'wren') then
    raise exception 'graveway: not a survivor' using errcode = '22023';
  end if;
  if p_distance is null or p_distance not between 1 and 100000
     or p_seconds is null or p_seconds not between 1 and 86400
     or p_kills is null or p_kills < 0 or coalesce(p_place, 0) < 0 then
    raise exception 'graveway: not a run' using errcode = '22023';
  end if;
  -- Possible? The fastest survivor, fully upgraded, makes under 10 m/s; the guns and the
  -- Black Market can't down more than a couple of dozen a second for long; a new place
  -- takes two bosses, a thousand metres.
  if p_distance > p_seconds * 12 + 30
     or p_kills > p_seconds * 25 + 50
     or coalesce(p_place, 0) > p_distance / 500 + 1 then
    raise exception 'graveway: that run is not possible' using errcode = '22023';
  end if;

  select * into v_run from graveway.scores where id = p_id;
  if found then
    -- The same run again: only its name can change.
    if v_run.distance <> p_distance or v_run.kills <> p_kills or v_run.survivor <> p_survivor then
      raise exception 'graveway: that run is already in' using errcode = '23505';
    end if;
    if not p_dry_run and v_run.name <> v_name then
      update graveway.scores set name = v_name, renamed_at = now() where id = p_id;
    end if;
    v_country := v_run.country;
  elsif not p_dry_run then
    if (select count(*) from graveway.scores where sender = v_sender and at > now() - interval '1 hour') >= 60 then
      raise exception 'graveway: too many runs from here, try again later' using errcode = '54000';
    end if;
    insert into graveway.scores (id, name, survivor, distance, kills, seconds, place, country, build, sender)
    values (p_id, v_name, p_survivor, p_distance, p_kills, p_seconds, coalesce(p_place, 0), v_country,
            coalesce(p_build, 0), v_sender);
  end if;

  return jsonb_build_object(
    'country', v_country,
    'world', graveway.rank(p_distance, p_kills, null),
    'region', case when v_country = '' then null else graveway.rank(p_distance, p_kills, v_country) end);
end
$$;

-- The best runs: p_scope 'world', or 'region' (the country it's asked from, or p_country).
-- Returns {"country": "<where it's asked from>", "region": "<the board's country, or null>",
-- "scores": [{"r": rank, "n": name, "s": survivor, "d": metres, "k": kills, "t": seconds,
-- "p": place, "c": country, "h": the first 12 of md5(id), so a phone can find its own}, ...]}.
create or replace function public.graveway_scores(p_scope text default 'world', p_limit integer default 50, p_country text default null)
returns jsonb language sql stable security definer set search_path = '' as $$
  with here as (select graveway.country() as cc),
  board as (
    select case when p_scope = 'region'
      then coalesce(nullif(upper(left(coalesce(p_country, ''), 2)), ''), (select cc from here))
      else null end as cc),
  picked as (
    select s.*, rank() over (order by s.distance desc, s.kills desc) as r
    from graveway.scores s, board b
    where coalesce(p_scope, 'world') <> 'region' or (b.cc <> '' and s.country = b.cc)
    order by s.distance desc, s.kills desc, s.at
    limit least(greatest(coalesce(p_limit, 50), 1), 100))
  select jsonb_build_object(
    'country', (select cc from here),
    'region', (select nullif(cc, '') from board),
    'scores', coalesce((
      select jsonb_agg(jsonb_build_object(
        'r', p.r, 'n', p.name, 's', p.survivor, 'd', p.distance, 'k', p.kills, 't', p.seconds,
        'p', p.place, 'c', p.country, 'h', left(md5(p.id::text), 12), 'at', p.at)
        order by p.distance desc, p.kills desc, p.at)
      from picked p), '[]'::jsonb))
$$;

revoke all on function public.graveway_submit_score(uuid, text, text, integer, integer, integer, integer, integer, boolean) from public;
revoke all on function public.graveway_scores(text, integer, text) from public;
grant execute on function public.graveway_submit_score(uuid, text, text, integer, integer, integer, integer, integer, boolean) to anon, authenticated;
grant execute on function public.graveway_scores(text, integer, text) to anon, authenticated;

-- PostgREST only sees new functions once it reloads its schema cache.
notify pgrst, 'reload schema';
