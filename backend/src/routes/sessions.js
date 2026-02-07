import express from 'express';
import db from '../db/database.js';

const router = express.Router();

// Roles con permisos de escritura de sesiones
const WRITE_ROLES = ['admin', 'org_admin', 'coach'];

// ─── GET /api/sessions ───────────────────────────────────────────
// Lista todas las sesiones del equipo del usuario autenticado
router.get('/', (req, res) => {
  try {
    const teamId = req.user.teamId;
    if (!teamId) {
      return res.status(400).json({ error: 'Usuario sin equipo asignado' });
    }

    const { since, includeDeleted } = req.query;

    let query = 'SELECT * FROM sessions WHERE team_id = ?';
    const params = [teamId];

    if (!includeDeleted) {
      query += ' AND is_deleted = 0';
    }

    if (since) {
      query += ' AND updated_at > ?';
      params.push(since);
    }

    query += ' ORDER BY session_date_utc DESC';

    const sessions = db.prepare(query).all(...params);

    res.json({
      success: true,
      sessions: sessions.map(formatSession),
      count: sessions.length
    });

  } catch (err) {
    console.error('[Sessions] Error listando sesiones:', err);
    res.status(500).json({ error: 'Error al listar sesiones' });
  }
});

// ─── GET /api/sessions/:id ───────────────────────────────────────
// Obtiene una sesión por su ID remoto
router.get('/:id', (req, res) => {
  try {
    const teamId = req.user.teamId;
    const sessionId = parseInt(req.params.id);

    const session = db.prepare(
      'SELECT * FROM sessions WHERE id = ? AND team_id = ?'
    ).get(sessionId, teamId);

    if (!session) {
      return res.status(404).json({ error: 'Sesión no encontrada' });
    }

    res.json({ success: true, session: formatSession(session) });

  } catch (err) {
    console.error('[Sessions] Error obteniendo sesión:', err);
    res.status(500).json({ error: 'Error al obtener sesión' });
  }
});

// ─── GET /api/sessions/by-local/:localId ─────────────────────────
// Obtiene una sesión por su ID local + deviceId (para verificar si ya existe)
router.get('/by-local/:localId', (req, res) => {
  try {
    const teamId = req.user.teamId;
    const localId = parseInt(req.params.localId);
    const { deviceId } = req.query;

    let query = 'SELECT * FROM sessions WHERE team_id = ? AND local_session_id = ?';
    const params = [teamId, localId];

    if (deviceId) {
      query += ' AND device_id = ?';
      params.push(deviceId);
    }

    const session = db.prepare(query).get(...params);

    if (!session) {
      return res.status(404).json({ error: 'Sesión no encontrada' });
    }

    res.json({ success: true, session: formatSession(session) });

  } catch (err) {
    console.error('[Sessions] Error buscando sesión local:', err);
    res.status(500).json({ error: 'Error al buscar sesión' });
  }
});

// ─── POST /api/sessions ─────────────────────────────────────────
// Crea o actualiza una sesión (upsert por team_id + local_session_id + device_id)
router.post('/', (req, res) => {
  try {
    const role = req.user.role;
    if (!WRITE_ROLES.includes(role)) {
      return res.status(403).json({ error: 'No tienes permisos para crear sesiones' });
    }

    const teamId = req.user.teamId;
    if (!teamId) {
      return res.status(400).json({ error: 'Usuario sin equipo asignado' });
    }

    const {
      localSessionId,
      deviceId,
      sessionName,
      place,
      coach,
      sessionType,
      sessionDateUtc,
      participants,
      isMerged,
      icon,
      iconColor,
      videoCount
    } = req.body;

    if (!localSessionId) {
      return res.status(400).json({ error: 'localSessionId es requerido' });
    }

    const now = new Date().toISOString();

    // Intentar upsert
    const existing = db.prepare(
      'SELECT id FROM sessions WHERE team_id = ? AND local_session_id = ? AND device_id = ?'
    ).get(teamId, localSessionId, deviceId || null);

    let sessionId;

    if (existing) {
      // Actualizar
      db.prepare(`
        UPDATE sessions SET
          session_name = ?,
          place = ?,
          coach = ?,
          session_type = ?,
          session_date_utc = ?,
          participants = ?,
          is_merged = ?,
          icon = ?,
          icon_color = ?,
          video_count = ?,
          updated_at = ?
        WHERE id = ?
      `).run(
        sessionName || null,
        place || null,
        coach || null,
        sessionType || null,
        sessionDateUtc || null,
        participants || null,
        isMerged || 0,
        icon || 'oar.2.crossed',
        iconColor || '#FFFFFFFF',
        videoCount || 0,
        now,
        existing.id
      );
      sessionId = existing.id;

      console.log(`[Sessions] Sesión actualizada: id=${sessionId}, local=${localSessionId}, name=${sessionName}`);
    } else {
      // Crear nueva
      const result = db.prepare(`
        INSERT INTO sessions (
          team_id, local_session_id, device_id,
          session_name, place, coach, session_type,
          session_date_utc, participants, is_merged,
          icon, icon_color, video_count,
          created_by_user_id, created_by_user_name,
          created_at, updated_at
        ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      `).run(
        teamId,
        localSessionId,
        deviceId || null,
        sessionName || null,
        place || null,
        coach || null,
        sessionType || null,
        sessionDateUtc || null,
        participants || null,
        isMerged || 0,
        icon || 'oar.2.crossed',
        iconColor || '#FFFFFFFF',
        videoCount || 0,
        req.user.userId,
        req.user.name,
        now,
        now
      );
      sessionId = result.lastInsertRowid;

      console.log(`[Sessions] Sesión creada: id=${sessionId}, local=${localSessionId}, name=${sessionName}`);
    }

    const session = db.prepare('SELECT * FROM sessions WHERE id = ?').get(sessionId);

    res.json({
      success: true,
      session: formatSession(session),
      isNew: !existing
    });

  } catch (err) {
    console.error('[Sessions] Error creando/actualizando sesión:', err);
    res.status(500).json({ error: 'Error al guardar sesión' });
  }
});

// ─── POST /api/sessions/batch ────────────────────────────────────
// Sincroniza múltiples sesiones de una vez (bulk upsert)
router.post('/batch', (req, res) => {
  try {
    const role = req.user.role;
    if (!WRITE_ROLES.includes(role)) {
      return res.status(403).json({ error: 'No tienes permisos para crear sesiones' });
    }

    const teamId = req.user.teamId;
    if (!teamId) {
      return res.status(400).json({ error: 'Usuario sin equipo asignado' });
    }

    const { sessions: sessionList } = req.body;
    if (!Array.isArray(sessionList) || sessionList.length === 0) {
      return res.status(400).json({ error: 'Se requiere un array de sesiones' });
    }

    const now = new Date().toISOString();
    const results = [];

    const insertStmt = db.prepare(`
      INSERT INTO sessions (
        team_id, local_session_id, device_id,
        session_name, place, coach, session_type,
        session_date_utc, participants, is_merged,
        icon, icon_color, video_count,
        created_by_user_id, created_by_user_name,
        created_at, updated_at
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
    `);

    const updateStmt = db.prepare(`
      UPDATE sessions SET
        session_name = ?,
        place = ?,
        coach = ?,
        session_type = ?,
        session_date_utc = ?,
        participants = ?,
        is_merged = ?,
        icon = ?,
        icon_color = ?,
        video_count = ?,
        updated_at = ?
      WHERE id = ?
    `);

    const findStmt = db.prepare(
      'SELECT id FROM sessions WHERE team_id = ? AND local_session_id = ? AND device_id = ?'
    );

    const batchTransaction = db.transaction((items) => {
      for (const s of items) {
        const existing = findStmt.get(teamId, s.localSessionId, s.deviceId || null);

        if (existing) {
          updateStmt.run(
            s.sessionName || null,
            s.place || null,
            s.coach || null,
            s.sessionType || null,
            s.sessionDateUtc || null,
            s.participants || null,
            s.isMerged || 0,
            s.icon || 'oar.2.crossed',
            s.iconColor || '#FFFFFFFF',
            s.videoCount || 0,
            now,
            existing.id
          );
          results.push({ localSessionId: s.localSessionId, remoteId: existing.id, isNew: false });
        } else {
          const result = insertStmt.run(
            teamId,
            s.localSessionId,
            s.deviceId || null,
            s.sessionName || null,
            s.place || null,
            s.coach || null,
            s.sessionType || null,
            s.sessionDateUtc || null,
            s.participants || null,
            s.isMerged || 0,
            s.icon || 'oar.2.crossed',
            s.iconColor || '#FFFFFFFF',
            s.videoCount || 0,
            req.user.userId,
            req.user.name,
            now,
            now
          );
          results.push({ localSessionId: s.localSessionId, remoteId: result.lastInsertRowid, isNew: true });
        }
      }
    });

    batchTransaction(sessionList);

    const created = results.filter(r => r.isNew).length;
    const updated = results.filter(r => !r.isNew).length;

    console.log(`[Sessions] Batch sync: ${created} creadas, ${updated} actualizadas`);

    res.json({
      success: true,
      results,
      created,
      updated,
      total: results.length
    });

  } catch (err) {
    console.error('[Sessions] Error en batch sync:', err);
    res.status(500).json({ error: 'Error en sincronización batch de sesiones' });
  }
});

// ─── PUT /api/sessions/:id ───────────────────────────────────────
// Actualiza una sesión existente por su ID remoto
router.put('/:id', (req, res) => {
  try {
    const role = req.user.role;
    if (!WRITE_ROLES.includes(role)) {
      return res.status(403).json({ error: 'No tienes permisos para modificar sesiones' });
    }

    const teamId = req.user.teamId;
    const sessionId = parseInt(req.params.id);

    const existing = db.prepare(
      'SELECT * FROM sessions WHERE id = ? AND team_id = ?'
    ).get(sessionId, teamId);

    if (!existing) {
      return res.status(404).json({ error: 'Sesión no encontrada' });
    }

    const {
      sessionName,
      place,
      coach,
      sessionType,
      sessionDateUtc,
      participants,
      isMerged,
      icon,
      iconColor,
      videoCount
    } = req.body;

    const now = new Date().toISOString();

    db.prepare(`
      UPDATE sessions SET
        session_name = COALESCE(?, session_name),
        place = COALESCE(?, place),
        coach = COALESCE(?, coach),
        session_type = COALESCE(?, session_type),
        session_date_utc = COALESCE(?, session_date_utc),
        participants = COALESCE(?, participants),
        is_merged = COALESCE(?, is_merged),
        icon = COALESCE(?, icon),
        icon_color = COALESCE(?, icon_color),
        video_count = COALESCE(?, video_count),
        updated_at = ?
      WHERE id = ?
    `).run(
      sessionName ?? null,
      place ?? null,
      coach ?? null,
      sessionType ?? null,
      sessionDateUtc ?? null,
      participants ?? null,
      isMerged ?? null,
      icon ?? null,
      iconColor ?? null,
      videoCount ?? null,
      now,
      sessionId
    );

    const session = db.prepare('SELECT * FROM sessions WHERE id = ?').get(sessionId);

    console.log(`[Sessions] Sesión ${sessionId} actualizada`);

    res.json({ success: true, session: formatSession(session) });

  } catch (err) {
    console.error('[Sessions] Error actualizando sesión:', err);
    res.status(500).json({ error: 'Error al actualizar sesión' });
  }
});

// ─── DELETE /api/sessions/:id ────────────────────────────────────
// Soft-delete de una sesión
router.delete('/:id', (req, res) => {
  try {
    const role = req.user.role;
    if (!WRITE_ROLES.includes(role)) {
      return res.status(403).json({ error: 'No tienes permisos para eliminar sesiones' });
    }

    const teamId = req.user.teamId;
    const sessionId = parseInt(req.params.id);

    const existing = db.prepare(
      'SELECT * FROM sessions WHERE id = ? AND team_id = ?'
    ).get(sessionId, teamId);

    if (!existing) {
      return res.status(404).json({ error: 'Sesión no encontrada' });
    }

    const now = new Date().toISOString();

    db.prepare(`
      UPDATE sessions SET is_deleted = 1, deleted_at = ?, updated_at = ? WHERE id = ?
    `).run(now, now, sessionId);

    console.log(`[Sessions] Sesión ${sessionId} eliminada (soft-delete)`);

    res.json({ success: true, message: 'Sesión eliminada' });

  } catch (err) {
    console.error('[Sessions] Error eliminando sesión:', err);
    res.status(500).json({ error: 'Error al eliminar sesión' });
  }
});

// ─── Helper: formatear sesión para la respuesta ──────────────────
function formatSession(row) {
  return {
    id: row.id,
    teamId: row.team_id,
    localSessionId: row.local_session_id,
    deviceId: row.device_id,
    sessionName: row.session_name,
    place: row.place,
    coach: row.coach,
    sessionType: row.session_type,
    sessionDateUtc: row.session_date_utc,
    participants: row.participants,
    isMerged: row.is_merged,
    icon: row.icon,
    iconColor: row.icon_color,
    videoCount: row.video_count,
    createdByUserId: row.created_by_user_id,
    createdByUserName: row.created_by_user_name,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    isDeleted: row.is_deleted,
    deletedAt: row.deleted_at
  };
}

export default router;
