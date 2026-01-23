import express from 'express';
import bcrypt from 'bcryptjs';
import jwt from 'jsonwebtoken';
import db from '../db/database.js';

const router = express.Router();

function normalizePlatform(platform) {
  if (!platform) return 'unknown';
  const value = String(platform).toLowerCase().trim();
  if (value.includes('ios') || value.includes('ipad') || value.includes('iphone')) return 'ios';
  if (value.includes('mac') || value.includes('maccatalyst')) return 'macos';
  if (value.includes('win')) return 'windows';
  if (value.includes('android')) return 'android';
  return value;
}

function getPlatformCategory(platform) {
  if (platform === 'ios') return 'ios';
  if (platform === 'windows' || platform === 'macos') return 'desktop';
  return 'desktop';
}

// POST /api/auth/register - Registrar nuevo usuario
router.post('/register', async (req, res) => {
  try {
    const { email, password, name, teamId = 'rfep' } = req.body;

    if (!email || !password || !name) {
      return res.status(400).json({ error: 'Email, contraseña y nombre son requeridos' });
    }

    // Verificar si el usuario ya existe
    const existingUser = db.prepare('SELECT id FROM users WHERE email = ?').get(email);
    if (existingUser) {
      return res.status(409).json({ error: 'El email ya está registrado' });
    }

    // Verificar que el equipo existe
    const team = db.prepare('SELECT * FROM teams WHERE id = ?').get(teamId);
    if (!team) {
      return res.status(400).json({ error: 'Equipo no válido' });
    }

    // Hash de la contraseña
    const passwordHash = await bcrypt.hash(password, 12);

    // Insertar usuario
    const result = db.prepare(
      'INSERT INTO users (email, password_hash, name, team_id) VALUES (?, ?, ?, ?)'
    ).run(email, passwordHash, name, teamId);

    console.log(`[Auth] Usuario registrado: ${email}`);

    res.status(201).json({
      success: true,
      message: 'Usuario registrado correctamente',
      userId: result.lastInsertRowid
    });

  } catch (err) {
    console.error('[Auth] Error en registro:', err);
    res.status(500).json({ error: 'Error al registrar usuario' });
  }
});

// POST /api/auth/login - Iniciar sesión
router.post('/login', async (req, res) => {
  try {
    const { email, password, deviceId, devicePlatform, deviceName } = req.body;

    if (!email || !password) {
      return res.status(400).json({ error: 'Email y contraseña son requeridos' });
    }

    // Buscar usuario
    const user = db.prepare(
      'SELECT u.*, t.name as team_name, t.wasabi_folder FROM users u LEFT JOIN teams t ON u.team_id = t.id WHERE u.email = ?'
    ).get(email);

    if (!user) {
      return res.status(401).json({ error: 'Credenciales inválidas' });
    }

    // Verificar contraseña
    const validPassword = await bcrypt.compare(password, user.password_hash);
    if (!validPassword) {
      return res.status(401).json({ error: 'Credenciales inválidas' });
    }

    // Validar y registrar dispositivo si se proporciona
    if (deviceId) {
      const normalizedPlatform = normalizePlatform(devicePlatform);
      const category = getPlatformCategory(normalizedPlatform);

      const existingDevice = db.prepare(
        'SELECT * FROM user_devices WHERE user_id = ? AND device_id = ?'
      ).get(user.id, deviceId);

      const activeDevices = db.prepare(
        'SELECT * FROM user_devices WHERE user_id = ? AND revoked_at IS NULL'
      ).all(user.id);

      const activeInCategory = activeDevices.filter(d => {
        const plat = normalizePlatform(d.platform);
        return getPlatformCategory(plat) === category;
      });

      const maxAllowed = category === 'ios' ? 1 : 1;
      const isExistingActive = existingDevice && !existingDevice.revoked_at;

      if (!isExistingActive && activeInCategory.length >= maxAllowed) {
        return res.status(403).json({
          error: category === 'ios'
            ? 'Límite de instalaciones iOS alcanzado (1 dispositivo).'
            : 'Límite de instalaciones desktop alcanzado (1 dispositivo).'
        });
      }

      if (existingDevice) {
        db.prepare(
          'UPDATE user_devices SET platform = ?, device_name = ?, last_seen = CURRENT_TIMESTAMP, revoked_at = NULL WHERE user_id = ? AND device_id = ?'
        ).run(normalizedPlatform, deviceName || null, user.id, deviceId);
      } else {
        db.prepare(
          'INSERT INTO user_devices (user_id, device_id, platform, device_name) VALUES (?, ?, ?, ?)'
        ).run(user.id, deviceId, normalizedPlatform, deviceName || null);
      }
    }

    // Actualizar último login
    db.prepare('UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE id = ?').run(user.id);

    // Generar JWT
    const accessToken = jwt.sign(
      {
        userId: user.id,
        email: user.email,
        name: user.name,
        teamId: user.team_id,
        teamName: user.team_name,
        wasabiFolder: user.wasabi_folder,
        role: user.role
      },
      process.env.JWT_SECRET,
      { expiresIn: '7d' }
    );

    // Refresh token (30 días)
    const refreshToken = jwt.sign(
      { userId: user.id },
      process.env.JWT_SECRET,
      { expiresIn: '30d' }
    );

    console.log(`[Auth] Login exitoso: ${email}`);

    res.json({
      success: true,
      accessToken,
      refreshToken,
      expiresIn: 7 * 24 * 60 * 60, // 7 días en segundos
      user: {
        id: user.id,
        email: user.email,
        name: user.name,
        teamId: user.team_id,
        teamName: user.team_name,
        role: user.role
      }
    });

  } catch (err) {
    console.error('[Auth] Error en login:', err);
    res.status(500).json({ error: 'Error al iniciar sesión' });
  }
});

// POST /api/auth/refresh - Refrescar token
router.post('/refresh', async (req, res) => {
  try {
    const { refreshToken } = req.body;

    if (!refreshToken) {
      return res.status(400).json({ error: 'Refresh token requerido' });
    }

    // Verificar refresh token
    const decoded = jwt.verify(refreshToken, process.env.JWT_SECRET);

    // Buscar usuario
    const user = db.prepare(
      'SELECT u.*, t.name as team_name, t.wasabi_folder FROM users u LEFT JOIN teams t ON u.team_id = t.id WHERE u.id = ?'
    ).get(decoded.userId);

    if (!user) {
      return res.status(401).json({ error: 'Usuario no encontrado' });
    }

    // Generar nuevo access token
    const accessToken = jwt.sign(
      {
        userId: user.id,
        email: user.email,
        name: user.name,
        teamId: user.team_id,
        teamName: user.team_name,
        wasabiFolder: user.wasabi_folder,
        role: user.role
      },
      process.env.JWT_SECRET,
      { expiresIn: '7d' }
    );

    res.json({
      success: true,
      accessToken,
      expiresIn: 7 * 24 * 60 * 60
    });

  } catch (err) {
    console.error('[Auth] Error en refresh:', err);
    res.status(401).json({ error: 'Refresh token inválido' });
  }
});

// GET /api/auth/me - Obtener usuario actual
router.get('/me', (req, res) => {
  const authHeader = req.headers['authorization'];
  const token = authHeader && authHeader.split(' ')[1];

  if (!token) {
    return res.status(401).json({ error: 'No autenticado' });
  }

  try {
    const user = jwt.verify(token, process.env.JWT_SECRET);
    res.json({
      id: user.userId,
      email: user.email,
      name: user.name,
      teamId: user.teamId,
      teamName: user.teamName,
      role: user.role
    });
  } catch (err) {
    res.status(401).json({ error: 'Token inválido' });
  }
});

export default router;
