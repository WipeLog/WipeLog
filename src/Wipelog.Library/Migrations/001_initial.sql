CREATE TABLE clips (
    id INTEGER PRIMARY KEY,
    video_path TEXT NOT NULL UNIQUE,
    sidecar_path TEXT NOT NULL,
    content_type TEXT NOT NULL,
    display_name TEXT NOT NULL,
    difficulty TEXT,
    encounter_id INTEGER,
    keystone_level INTEGER,
    success INTEGER NOT NULL,
    started_at TEXT NOT NULL,
    ended_at TEXT NOT NULL,
    duration_seconds INTEGER NOT NULL,
    file_size_bytes INTEGER NOT NULL,
    favorite INTEGER NOT NULL DEFAULT 0,
    indexed_at TEXT NOT NULL
);

CREATE INDEX idx_clips_started_at ON clips(started_at DESC);
CREATE INDEX idx_clips_content_type ON clips(content_type);
CREATE INDEX idx_clips_favorite ON clips(favorite) WHERE favorite = 1;

CREATE TABLE tags (
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE COLLATE NOCASE
);

CREATE TABLE clip_tags (
    clip_id INTEGER NOT NULL REFERENCES clips(id) ON DELETE CASCADE,
    tag_id INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    PRIMARY KEY (clip_id, tag_id)
);

PRAGMA user_version = 1;
