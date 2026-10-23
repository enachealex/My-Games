-- Checks scoreboard.sql in place, as the API's own role, inside a transaction that's rolled
-- back: nothing it does is kept. Any failed check stops it with an error. Its runs are far
-- longer than anyone's (80-90 km), so they top the boards whatever's already on them.
--   docker exec -i supabase-db psql -U postgres -d postgres -v ON_ERROR_STOP=1 < scoreboard-test.sql

begin;
set local role anon;

-- As if through Cloudflare, from the US.
select set_config('request.headers', '{"cf-ipcountry":"US","cf-connecting-ip":"203.0.113.7"}', true);

do $$
declare r jsonb; b jsonb; n int;
begin
  -- The schema itself is out of the API's reach.
  begin
    perform count(*) from graveway.scores;
    raise exception 'FAIL: anon can read graveway.scores';
  exception when insufficient_privilege then null;
  end;

  -- A dry run is ranked but not kept.
  r := public.graveway_submit_score('00000000-0000-4000-8000-000000000001', 'Dry', 'rook', 70000, 40, 8000, 0, 8, true);
  if r->>'country' <> 'US' then raise exception 'FAIL: country %', r; end if;

  -- Two runs, a name cleaned, and ranks in the world and the region.
  r := public.graveway_submit_score('00000000-0000-4000-8000-000000000002', E'  Sam\tthe   Great  ', 'wren', 80000, 300, 8000, 0, 8);
  r := public.graveway_submit_score('00000000-0000-4000-8000-000000000003', 'Alex', 'bram', 90000, 500, 8000, 1, 8);
  if (r->>'world')::int <> 1 or (r->>'region')::int <> 1 then raise exception 'FAIL: ranks %', r; end if;

  -- Renamed after it was sent: the same run, a new name.
  r := public.graveway_submit_score('00000000-0000-4000-8000-000000000002', 'Sam', 'wren', 80000, 300, 8000, 0, 8);
  if (r->>'world')::int <> 2 then raise exception 'FAIL: renamed run rank %', r; end if;

  -- The same id can't be a better run.
  begin
    r := public.graveway_submit_score('00000000-0000-4000-8000-000000000002', 'Sam', 'wren', 85000, 300, 8000, 0, 8);
    raise exception 'FAIL: an id was reused for a better run';
  exception when unique_violation then null;
  end;

  -- Impossible runs are turned away.
  begin
    r := public.graveway_submit_score('00000000-0000-4000-8000-000000000004', 'Fast', 'wren', 5000, 10, 60, 0, 8);
    raise exception 'FAIL: 5 km in a minute was taken';
  exception when invalid_parameter_value then null;
  end;
  begin
    r := public.graveway_submit_score('00000000-0000-4000-8000-000000000005', 'Who', 'nobody', 100, 1, 60, 0, 8);
    raise exception 'FAIL: an unknown survivor was taken';
  exception when invalid_parameter_value then null;
  end;

  -- From somewhere else: Canada.
  perform set_config('request.headers', '{"cf-ipcountry":"CA","cf-connecting-ip":"198.51.100.9"}', true);
  r := public.graveway_submit_score('00000000-0000-4000-8000-000000000006', 'Jo', 'juno', 85000, 100, 8000, 0, 8);
  if (r->>'world')::int <> 2 or (r->>'region')::int <> 1 or r->>'country' <> 'CA' then raise exception 'FAIL: Canada %', r; end if;

  b := public.graveway_scores('world', 50);
  select count(*) into n from jsonb_array_elements(b->'scores') e where e->>'h' in (
    left(md5('00000000-0000-4000-8000-000000000002'), 12), left(md5('00000000-0000-4000-8000-000000000003'), 12),
    left(md5('00000000-0000-4000-8000-000000000006'), 12));
  if n <> 3 then raise exception 'FAIL: world board %', b; end if;
  if b->'scores'->0->>'n' <> 'Alex' or b->'scores'->1->>'n' <> 'Jo' then raise exception 'FAIL: best first %', b; end if;
  if exists (select 1 from jsonb_array_elements(b->'scores') e where e->>'n' = 'Dry') then raise exception 'FAIL: the dry run was kept'; end if;
  if not exists (select 1 from jsonb_array_elements(b->'scores') e
                 where e->>'n' = 'Sam' and e->>'h' = left(md5('00000000-0000-4000-8000-000000000002'), 12)) then
    raise exception 'FAIL: the rename %', b;
  end if;

  -- The region board is the asker's country (Canada now), or the one asked for.
  b := public.graveway_scores('region', 50);
  if b->>'region' <> 'CA' or exists (select 1 from jsonb_array_elements(b->'scores') e where e->>'c' <> 'CA') then
    raise exception 'FAIL: Canada board %', b;
  end if;
  b := public.graveway_scores('region', 50, 'us');
  if b->>'region' <> 'US' or exists (select 1 from jsonb_array_elements(b->'scores') e where e->>'c' <> 'US') then
    raise exception 'FAIL: US board %', b;
  end if;

  -- Unknown country: no region board.
  perform set_config('request.headers', '{"cf-ipcountry":"XX"}', true);
  b := public.graveway_scores('region', 50);
  if jsonb_array_length(b->'scores') <> 0 then raise exception 'FAIL: unknown-country board %', b; end if;

  raise notice 'graveway scoreboard: all checks passed';
end
$$;

rollback;
