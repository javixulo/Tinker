CREATE TABLE devices (
  device_id         INTEGER PRIMARY KEY AUTOINCREMENT,
  brand             TEXT NOT NULL,
  model             TEXT NOT NULL,
  brand_key         TEXT NOT NULL,
  model_key         TEXT NOT NULL,
  device_type       TEXT,
  color             TEXT,
  location          TEXT,
  invoice           TEXT,
  purchase_date     TEXT,
  warranty_end_date TEXT,
  status            TEXT CHECK (status IN ('active','stored','sold','discarded')),
  UNIQUE (brand_key, model_key)
);

CREATE TABLE device_manuals (
  device_id INTEGER NOT NULL REFERENCES devices(device_id) ON DELETE CASCADE,
  path      TEXT NOT NULL UNIQUE
);

CREATE TABLE device_urls (
  device_id INTEGER NOT NULL REFERENCES devices(device_id) ON DELETE CASCADE,
  url       TEXT NOT NULL
);

CREATE TABLE chunks (
  chunk_id  TEXT PRIMARY KEY,
  device_id INTEGER NOT NULL,
  manual    TEXT NOT NULL,
  page      INTEGER NOT NULL,
  text      TEXT NOT NULL,
  embedding BLOB NOT NULL
);

CREATE INDEX ix_chunks_device ON chunks(device_id);

CREATE TABLE index_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
