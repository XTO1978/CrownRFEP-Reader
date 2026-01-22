// Seleccionar implementación según disponibilidad de better-sqlite3
let db;

try {
  // Intentar usar better-sqlite3 (mejor rendimiento)
  const Database = (await import('better-sqlite3')).default;
  const path = await import('path');
  const { fileURLToPath } = await import('url');
  const fs = await import('fs');

  const __dirname = path.dirname(fileURLToPath(import.meta.url));
  const dbPath = path.join(__dirname, '../../data/users.db');

  // Asegurar que existe el directorio data
  const dataDir = path.join(__dirname, '../../data');
  if (!fs.existsSync(dataDir)) {
    fs.mkdirSync(dataDir, { recursive: true });
  }

  db = new Database(dbPath);

  // Crear tabla de usuarios si no existe
  db.exec(`
    CREATE TABLE IF NOT EXISTS users (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      email TEXT UNIQUE NOT NULL,
      password_hash TEXT NOT NULL,
      name TEXT NOT NULL,
      role TEXT DEFAULT 'user',
      team_id TEXT,
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
      last_login DATETIME
    )
  `);

  // Crear tabla de equipos si no existe
  db.exec(`
    CREATE TABLE IF NOT EXISTS teams (
      id TEXT PRIMARY KEY,
      name TEXT NOT NULL,
      wasabi_folder TEXT NOT NULL,
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    )
  `);

  // Insertar equipo RFEP por defecto si no existe
  const rfepTeam = db.prepare('SELECT * FROM teams WHERE id = ?').get('rfep');
  if (!rfepTeam) {
    db.prepare('INSERT INTO teams (id, name, wasabi_folder) VALUES (?, ?, ?)')
      .run('rfep', 'Real Federación Española de Piragüismo', 'CrownRFEP');
    console.log('[DB] Equipo RFEP creado (SQLite)');
  }
  
  console.log('[DB] Usando better-sqlite3');
} catch (err) {
  // Fallback a implementación JSON si better-sqlite3 no está disponible
  console.log('[DB] better-sqlite3 no disponible, usando JSON:', err.message);
  db = (await import('./database-json.js')).default;
}

export default db;
