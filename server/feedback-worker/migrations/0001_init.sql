CREATE TABLE feedback (
  id          INTEGER PRIMARY KEY AUTOINCREMENT,
  created_at  TEXT NOT NULL,
  kind        TEXT NOT NULL CHECK (kind IN ('suggestion', 'error', 'other')),
  message     TEXT NOT NULL,
  contact     TEXT,
  app_version TEXT NOT NULL,
  logs        TEXT,
  ip_hash     TEXT NOT NULL
);

CREATE INDEX idx_feedback_created_at ON feedback (created_at);
CREATE INDEX idx_feedback_ip_created ON feedback (ip_hash, created_at);
