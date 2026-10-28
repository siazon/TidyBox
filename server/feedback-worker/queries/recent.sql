SELECT id, created_at, kind, substr(message, 1, 80) AS message, contact, app_version, length(logs) AS log_bytes
FROM feedback
ORDER BY id DESC
LIMIT 50;
