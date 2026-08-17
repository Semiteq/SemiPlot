-- The Simple-Scada 2 archive schema, `trends` only.
--
-- Extracted from a customer archive dump with `pg_restore --schema-only`; see README.md for the
-- command and for what was stripped. This is the vendor's definition, not ours: SemiPlot is a
-- read-only consumer and never alters these objects (docs/architecture/scada-archive.md).
--
-- `messages` is excluded on purpose — no slice of the PostgreSQL data source reads it.
--
-- Day partitions `tpYYYYmMMdDD` are created by the seeder for the days a run covers. Only the
-- `tpdefault` catch-all is created here; rows landing in it signal a missing day partition.

CREATE TABLE public.trends (
    id integer DEFAULT 0 NOT NULL,
    l smallint DEFAULT 0 NOT NULL,
    t timestamp(3) without time zone NOT NULL,
    v double precision,
    q integer NOT NULL
)
PARTITION BY RANGE (t);

ALTER TABLE ONLY public.trends
    ADD CONSTRAINT tpk PRIMARY KEY (id, l, t);

CREATE TABLE public.tpdefault PARTITION OF public.trends DEFAULT;
