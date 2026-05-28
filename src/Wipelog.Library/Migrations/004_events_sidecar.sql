ALTER TABLE clips ADD COLUMN events_sidecar_path TEXT;
ALTER TABLE clips ADD COLUMN has_events INTEGER NOT NULL DEFAULT 0;

PRAGMA user_version = 4;
