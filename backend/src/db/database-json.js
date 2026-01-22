// Base de datos simple usando archivos JSON
// Alternativa a better-sqlite3 para servidores con recursos limitados

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const dataDir = path.join(__dirname, '../../data');
const usersFile = path.join(dataDir, 'users.json');
const teamsFile = path.join(dataDir, 'teams.json');
const rolesFile = path.join(dataDir, 'roles.json');

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
let roles = loadJson(rolesFile, []);

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

// Inicializar roles por defecto si no existen
const defaultRoles = [
  {
    id: 'admin',
    name: 'Administrador Root',
    description: 'Acceso total al sistema',
    permissions_json: JSON.stringify(['*']),
    created_at: new Date().toISOString()
  },
  {
    id: 'org_admin',
    name: 'Administrador de Organización',
    description: 'CRUD total en Wasabi (solo su organización) y gestión de cuentas de su organización',
    permissions_json: JSON.stringify(['org:manage', 'wasabi:crud', 'users:crud']),
    created_at: new Date().toISOString()
  },
  {
    id: 'coach',
    name: 'Entrenador',
    description: 'CRUD en sesiones y archivos de video desde la app',
    permissions_json: JSON.stringify(['sessions:crud', 'videos:crud', 'metadata:crud']),
    created_at: new Date().toISOString()
  },
  {
    id: 'athlete',
    name: 'Atleta',
    description: 'Solo lectura en Wasabi',
    permissions_json: JSON.stringify(['wasabi:read']),
    created_at: new Date().toISOString()
  }
];

for (const role of defaultRoles) {
  if (!roles.find(r => r.id === role.id)) {
    roles.push(role);
  }
}
saveJson(rolesFile, roles);

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
        // UPDATE user
        if (sql.includes('UPDATE users SET')) {
          const id = params[params.length - 1];
          const user = users.find(u => String(u.id) === String(id));
          if (!user) return { changes: 0 };

          const setters = sql.split('SET')[1].split('WHERE')[0].split(',').map(s => s.trim());
          setters.forEach((setter, index) => {
            const field = setter.split('=')[0].trim();
            user[field] = params[index];
          });

          saveJson(usersFile, users);
          return { changes: 1 };
        }
        // DELETE user
        if (sql.includes('DELETE FROM users')) {
          const [id] = params;
          const before = users.length;
          users = users.filter(u => String(u.id) !== String(id));
          saveJson(usersFile, users);
          return { changes: before - users.length };
        }
        // INSERT role
        if (sql.includes('INSERT INTO roles')) {
          const [id, name, description, permissionsJson] = params;
          const newRole = {
            id,
            name,
            description,
            permissions_json: permissionsJson,
            created_at: new Date().toISOString()
          };
          roles.push(newRole);
          saveJson(rolesFile, roles);
          return { changes: 1 };
        }
        // UPDATE role
        if (sql.includes('UPDATE roles SET')) {
          const id = params[params.length - 1];
          const role = roles.find(r => r.id === id);
          if (!role) return { changes: 0 };

          const setters = sql.split('SET')[1].split('WHERE')[0].split(',').map(s => s.trim());
          setters.forEach((setter, index) => {
            const field = setter.split('=')[0].trim();
            role[field] = params[index];
          });

          saveJson(rolesFile, roles);
          return { changes: 1 };
        }
        // DELETE role
        if (sql.includes('DELETE FROM roles')) {
          const [id] = params;
          const before = roles.length;
          roles = roles.filter(r => r.id !== id);
          saveJson(rolesFile, roles);
          return { changes: before - roles.length };
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
        // SELECT role by id
        if (sql.includes('FROM roles WHERE id')) {
          const [id] = params;
          return roles.find(r => r.id === id);
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
        // SELECT all roles
        if (sql.includes('SELECT * FROM roles')) {
          return roles;
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
