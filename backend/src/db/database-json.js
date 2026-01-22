// Base de datos simple usando archivos JSON
// Alternativa a better-sqlite3 para servidores con recursos limitados

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const dataDir = path.join(__dirname, '../../data');
const usersFile = path.join(dataDir, 'users.json');
const teamsFile = path.join(dataDir, 'teams.json');

// Asegurar que existe el directorio data
if (!fs.existsSync(dataDir)) {
  fs.mkdirSync(dataDir, { recursive: true });
}

// Cargar o inicializar datos
function loadJson(filePath, defaultData = []) {
  try {
    if (fs.existsSync(filePath)) {
      return JSON.parse(fs.readFileSync(filePath, 'utf-8'));
    }
  } catch (err) {
    console.error(`[DB] Error cargando ${filePath}:`, err);
  }
  return defaultData;
}

function saveJson(filePath, data) {
  fs.writeFileSync(filePath, JSON.stringify(data, null, 2), 'utf-8');
}

// Datos en memoria
let users = loadJson(usersFile, []);
let teams = loadJson(teamsFile, []);

// Inicializar equipo RFEP si no existe
if (!teams.find(t => t.id === 'rfep')) {
  teams.push({
    id: 'rfep',
    name: 'Real Federación Española de Piragüismo',
    wasabi_folder: 'CrownRFEP',
    created_at: new Date().toISOString()
  });
  saveJson(teamsFile, teams);
  console.log('[DB] Equipo RFEP creado');
}

// API similar a better-sqlite3
const db = {
  // Prepare statement mock
  prepare(sql) {
    return {
      run(...params) {
        // INSERT user
        if (sql.includes('INSERT INTO users')) {
          let email, passwordHash, name, role, teamId;
          if (params.length === 4) {
            [email, passwordHash, name, teamId] = params;
            role = 'user';
          } else {
            [email, passwordHash, name, role, teamId] = params;
          }
          const newUser = {
            id: users.length > 0 ? Math.max(...users.map(u => u.id)) + 1 : 1,
            email,
            password_hash: passwordHash,
            name,
            role: role || 'user',
            team_id: teamId,
            created_at: new Date().toISOString(),
            last_login: null
          };
          users.push(newUser);
          saveJson(usersFile, users);
          return { changes: 1, lastInsertRowid: newUser.id };
        }
        // UPDATE last_login
        if (sql.includes('UPDATE users SET last_login')) {
          const [lastLogin, id] = params;
          const user = users.find(u => u.id === id);
          if (user) {
            user.last_login = lastLogin;
            saveJson(usersFile, users);
            return { changes: 1 };
          }
          return { changes: 0 };
        }
        return { changes: 0 };
      },
      get(...params) {
        // SELECT user by email
        if (sql.includes('FROM users WHERE email')) {
          const [email] = params;
          return users.find(u => u.email === email);
        }
        // SELECT user by id
        if (sql.includes('FROM users WHERE id')) {
          const [id] = params;
          return users.find(u => u.id === id);
        }
        // SELECT team by id
        if (sql.includes('FROM teams WHERE id')) {
          const [id] = params;
          return teams.find(t => t.id === id);
        }
        return undefined;
      },
      all(...params) {
        // SELECT all users
        if (sql.includes('SELECT * FROM users')) {
          return users;
        }
        // SELECT all teams
        if (sql.includes('SELECT * FROM teams')) {
          return teams;
        }
        return [];
      }
    };
  },
  
  exec(sql) {
    // No-op para CREATE TABLE, etc.
    console.log('[DB] exec:', sql.substring(0, 50) + '...');
  }
};

export default db;
