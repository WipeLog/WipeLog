ALTER TABLE clips ADD COLUMN player_region TEXT;

CREATE INDEX idx_clips_player_region ON clips(player_region);

PRAGMA user_version = 3;
