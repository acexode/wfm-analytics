import { existsSync, mkdirSync, writeFileSync } from 'node:fs';
import { randomBytes } from 'node:crypto';
import { fileURLToPath } from 'node:url';

// Only development credentials are generated here. Never write company credentials.
const directory = fileURLToPath(new URL('../.local/', import.meta.url));
mkdirSync(directory, { recursive: true, mode: 0o700 });
const file = `${directory}database.env`;
if (!existsSync(file)) {
  const migration = randomBytes(24).toString('hex');
  const runtime = randomBytes(24).toString('hex');
  writeFileSync(`${directory}migration-password`, migration, { mode: 0o600 });
  writeFileSync(file, `WFM_MIGRATION_PASSWORD=${migration}\nWFM_APP_PASSWORD=${runtime}\n`, { mode: 0o600 });
  console.log('Created local development credentials in ignored .local directory.');
}
