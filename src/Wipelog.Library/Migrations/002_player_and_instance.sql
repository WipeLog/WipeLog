ALTER TABLE clips ADD COLUMN player_name TEXT;
ALTER TABLE clips ADD COLUMN player_realm TEXT;
ALTER TABLE clips ADD COLUMN player_class TEXT;
ALTER TABLE clips ADD COLUMN player_spec TEXT;
ALTER TABLE clips ADD COLUMN instance_name TEXT;
ALTER TABLE clips ADD COLUMN dungeon_name TEXT;

CREATE INDEX idx_clips_player_name ON clips(player_name);
CREATE INDEX idx_clips_dungeon_name ON clips(dungeon_name);
CREATE INDEX idx_clips_instance_name ON clips(instance_name);

PRAGMA user_version = 2;
