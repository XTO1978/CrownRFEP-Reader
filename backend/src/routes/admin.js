import express from 'express';
import db from '../db/database.js';
import bcrypt from 'bcryptjs';
import { S3Client, ListObjectsV2Command } from '@aws-sdk/client-s3';

const router = express.Router();

let s3Client = null;

function getS3Client() {
  if (!s3Client) {
    s3Client = new S3Client({
      region: process.env.WASABI_REGION || 'eu-west-2',
      endpoint: process.env.WASABI_ENDPOINT || 'https://s3.eu-west-2.wasabisys.com',
      credentials: {
        accessKeyId: process.env.WASABI_ACCESS_KEY,
        secretAccessKey: process.env.WASABI_SECRET_KEY
      },
      forcePathStyle: true
    });
  }
  return s3Client;
}

function getBucket() {
  return process.env.WASABI_BUCKET || 'crownanalyzer';
}

function safeDbAll(sql, params = []) {
  try {
    return db.prepare(sql).all(...params);
  } catch (err) {
    return null;
  }
}

function safeDbGet(sql, params = []) {
  try {
    return db.prepare(sql).get(...params);
  } catch (err) {
    return null;
  }
}

function safeDbRun(sql, params = []) {
  try {
    return db.prepare(sql).run(...params);
  } catch (err) {
    return null;
  }
}

router.get('/users', (req, res) => {
  try {
    const rows = safeDbAll(
      'SELECT u.id, u.email, u.name, u.role, u.team_id, u.created_at, u.last_login, t.name as team_name, t.wasabi_folder FROM users u LEFT JOIN teams t ON u.team_id = t.id'
    );

    if (rows) {
      return res.json({ success: true, users: rows });
    }

    const users = db.prepare('SELECT * FROM users').all();
    const teams = db.prepare('SELECT * FROM teams').all();

    const mapped = users.map(u => {
      const team = teams.find(t => t.id === u.team_id);
      return {
        id: u.id,
        email: u.email,
        name: u.name,
        role: u.role,
        team_id: u.team_id,
        created_at: u.created_at,
        last_login: u.last_login,
        team_name: team?.name || null,
        wasabi_folder: team?.wasabi_folder || null
      };
    });

    res.json({ success: true, users: mapped });
  } catch (err) {
    console.error('[Admin] Error listando usuarios:', err);
    res.status(500).json({ error: 'Error listando usuarios' });
  }
});

router.post('/users', async (req, res) => {
  try {
    const { email, password, name, role = 'user', teamId = 'rfep' } = req.body;

    if (!email || !password || !name) {
      return res.status(400).json({ error: 'Email, contraseña y nombre son requeridos' });
    }

    const existing = safeDbGet('SELECT id FROM users WHERE email = ?', [email]);
    if (existing) {
      return res.status(409).json({ error: 'Email ya existe' });
    }

    const passwordHash = await bcrypt.hash(password, 12);
    const result = safeDbRun(
      'INSERT INTO users (email, password_hash, name, role, team_id) VALUES (?, ?, ?, ?, ?)',
      [email, passwordHash, name, role, teamId]
    );

    if (!result) {
      return res.status(500).json({ error: 'No se pudo crear usuario' });
    }

    res.json({ success: true, userId: result.lastInsertRowid });
  } catch (err) {
    console.error('[Admin] Error creando usuario:', err);
    res.status(500).json({ error: 'Error creando usuario' });
  }
});

router.patch('/users/:id', async (req, res) => {
  try {
    const { id } = req.params;
    const { name, role, teamId, password } = req.body;

    const fields = [];
    const params = [];

    if (name) { fields.push('name = ?'); params.push(name); }
    if (role) { fields.push('role = ?'); params.push(role); }
    if (teamId) { fields.push('team_id = ?'); params.push(teamId); }

    if (password) {
      const passwordHash = await bcrypt.hash(password, 12);
      fields.push('password_hash = ?');
      params.push(passwordHash);
    }

    if (fields.length === 0) {
      return res.status(400).json({ error: 'Nada que actualizar' });
    }

    params.push(id);
    const result = safeDbRun(`UPDATE users SET ${fields.join(', ')} WHERE id = ?`, params);

    if (!result) {
      return res.status(500).json({ error: 'No se pudo actualizar usuario' });
    }

    res.json({ success: true });
  } catch (err) {
    console.error('[Admin] Error actualizando usuario:', err);
    res.status(500).json({ error: 'Error actualizando usuario' });
  }
});

router.delete('/users/:id', (req, res) => {
  try {
    const { id } = req.params;
    if (String(req.user?.userId) === String(id)) {
      return res.status(400).json({ error: 'No puedes eliminar tu propio usuario' });
    }

    const result = safeDbRun('DELETE FROM users WHERE id = ?', [id]);
    if (!result) {
      return res.status(500).json({ error: 'No se pudo eliminar usuario' });
    }
    res.json({ success: true });
  } catch (err) {
    console.error('[Admin] Error eliminando usuario:', err);
    res.status(500).json({ error: 'Error eliminando usuario' });
  }
});

router.get('/teams', (req, res) => {
  try {
    const teams = db.prepare('SELECT * FROM teams').all();
    res.json({ success: true, teams });
  } catch (err) {
    console.error('[Admin] Error listando equipos:', err);
    res.status(500).json({ error: 'Error listando equipos' });
  }
});

router.post('/teams', (req, res) => {
  try {
    const { id, name, wasabiFolder } = req.body;
    if (!id || !name || !wasabiFolder) {
      return res.status(400).json({ error: 'ID, nombre y carpeta Wasabi son requeridos' });
    }

    const existing = safeDbGet('SELECT id FROM teams WHERE id = ?', [id]);
    if (existing) {
      return res.status(409).json({ error: 'Organización ya existe' });
    }

    const result = safeDbRun(
      'INSERT INTO teams (id, name, wasabi_folder) VALUES (?, ?, ?)',
      [id, name, wasabiFolder]
    );

    if (!result) {
      return res.status(500).json({ error: 'No se pudo crear organización' });
    }

    res.json({ success: true });
  } catch (err) {
    console.error('[Admin] Error creando organización:', err);
    res.status(500).json({ error: 'Error creando organización' });
  }
});

router.patch('/teams/:id', (req, res) => {
  try {
    const { id } = req.params;
    const { name, wasabiFolder } = req.body;

    const fields = [];
    const params = [];

    if (name) { fields.push('name = ?'); params.push(name); }
    if (wasabiFolder) { fields.push('wasabi_folder = ?'); params.push(wasabiFolder); }

    if (fields.length === 0) {
      return res.status(400).json({ error: 'Nada que actualizar' });
    }

    params.push(id);
    const result = safeDbRun(`UPDATE teams SET ${fields.join(', ')} WHERE id = ?`, params);

    if (!result) {
      return res.status(500).json({ error: 'No se pudo actualizar organización' });
    }

    res.json({ success: true });
  } catch (err) {
    console.error('[Admin] Error actualizando organización:', err);
    res.status(500).json({ error: 'Error actualizando organización' });
  }
});

router.delete('/teams/:id', (req, res) => {
  try {
    const { id } = req.params;

    // Verificar si hay usuarios asignados a esta organización
    const usersInTeam = safeDbAll('SELECT id FROM users WHERE team_id = ?', [id]);
    if (usersInTeam && usersInTeam.length > 0) {
      return res.status(400).json({ error: 'No se puede eliminar: hay usuarios asignados a esta organización' });
    }

    const result = safeDbRun('DELETE FROM teams WHERE id = ?', [id]);
    if (!result) {
      return res.status(500).json({ error: 'No se pudo eliminar organización' });
    }

    res.json({ success: true });
  } catch (err) {
    console.error('[Admin] Error eliminando organización:', err);
    res.status(500).json({ error: 'Error eliminando organización' });
  }
});

router.get('/roles', (req, res) => {
  try {
    const roles = db.prepare('SELECT * FROM roles').all();
    res.json({ success: true, roles });
  } catch (err) {
    console.error('[Admin] Error listando roles:', err);
    res.status(500).json({ error: 'Error listando roles' });
  }
});

router.post('/roles', (req, res) => {
  try {
    const { id, name, description, permissions = [] } = req.body;
    if (!id || !name) {
      return res.status(400).json({ error: 'ID y nombre son requeridos' });
    }

    const existing = safeDbGet('SELECT id FROM roles WHERE id = ?', [id]);
    if (existing) {
      return res.status(409).json({ error: 'Rol ya existe' });
    }

    const result = safeDbRun(
      'INSERT INTO roles (id, name, description, permissions_json) VALUES (?, ?, ?, ?)',
      [id, name, description || '', JSON.stringify(permissions)]
    );

    if (!result) {
      return res.status(500).json({ error: 'No se pudo crear rol' });
    }

    res.json({ success: true });
  } catch (err) {
    console.error('[Admin] Error creando rol:', err);
    res.status(500).json({ error: 'Error creando rol' });
  }
});

router.patch('/roles/:id', (req, res) => {
  try {
    const { id } = req.params;
    const { name, description, permissions } = req.body;

    const fields = [];
    const params = [];

    if (name) { fields.push('name = ?'); params.push(name); }
    if (description !== undefined) { fields.push('description = ?'); params.push(description); }
    if (permissions) { fields.push('permissions_json = ?'); params.push(JSON.stringify(permissions)); }

    if (fields.length === 0) {
      return res.status(400).json({ error: 'Nada que actualizar' });
    }

    params.push(id);
    const result = safeDbRun(`UPDATE roles SET ${fields.join(', ')} WHERE id = ?`, params);
    if (!result) {
      return res.status(500).json({ error: 'No se pudo actualizar rol' });
    }

    res.json({ success: true });
  } catch (err) {
    console.error('[Admin] Error actualizando rol:', err);
    res.status(500).json({ error: 'Error actualizando rol' });
  }
});

router.delete('/roles/:id', (req, res) => {
  try {
    const { id } = req.params;
    if (id === 'admin') {
      return res.status(400).json({ error: 'No se puede eliminar el rol admin' });
    }

    const result = safeDbRun('DELETE FROM roles WHERE id = ?', [id]);
    if (!result) {
      return res.status(500).json({ error: 'No se pudo eliminar rol' });
    }

    res.json({ success: true });
  } catch (err) {
    console.error('[Admin] Error eliminando rol:', err);
    res.status(500).json({ error: 'Error eliminando rol' });
  }
});

router.get('/metrics', (req, res) => {
  try {
    const users = db.prepare('SELECT * FROM users').all();
    const teams = db.prepare('SELECT * FROM teams').all();
    res.json({
      success: true,
      totals: {
        users: users.length,
        teams: teams.length
      }
    });
  } catch (err) {
    console.error('[Admin] Error métricas:', err);
    res.status(500).json({ error: 'Error métricas' });
  }
});

router.get('/wasabi/stats', async (req, res) => {
  try {
    const teams = db.prepare('SELECT * FROM teams').all();
    const client = getS3Client();

    const results = [];
    let totalBytes = 0;
    let totalObjects = 0;

    for (const team of teams) {
      const prefix = `${team.wasabi_folder}/`;
      let continuationToken = undefined;
      let teamBytes = 0;
      let teamObjects = 0;
      let lastModified = null;

      do {
        const command = new ListObjectsV2Command({
          Bucket: getBucket(),
          Prefix: prefix,
          ContinuationToken: continuationToken
        });

        const response = await client.send(command);
        const items = response.Contents || [];

        for (const item of items) {
          teamObjects += 1;
          teamBytes += item.Size || 0;
          if (!lastModified || (item.LastModified && item.LastModified > lastModified)) {
            lastModified = item.LastModified;
          }
        }

        continuationToken = response.IsTruncated ? response.NextContinuationToken : undefined;
      } while (continuationToken);

      totalBytes += teamBytes;
      totalObjects += teamObjects;

      results.push({
        teamId: team.id,
        teamName: team.name,
        wasabiFolder: team.wasabi_folder,
        objectCount: teamObjects,
        totalBytes: teamBytes,
        lastModified
      });
    }

    res.json({
      success: true,
      totals: { totalBytes, totalObjects },
      teams: results
    });
  } catch (err) {
    console.error('[Admin] Error Wasabi stats:', err);
    res.status(500).json({ error: 'Error stats Wasabi' });
  }
});

export default router;
