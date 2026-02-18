import express from 'express';
import { S3Client, GetObjectCommand, PutObjectCommand, HeadObjectCommand, ListObjectsV2Command } from '@aws-sdk/client-s3';
import db from '../db/database.js';

const router = express.Router();

// Cliente S3 lazy (reutilizado)
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

// ─── GET /api/team/info ───────────────────────────────────────────
// Devuelve información del equipo del usuario autenticado
router.get('/info', async (req, res) => {
  try {
    const teamId = req.user.teamId;
    if (!teamId) {
      return res.status(400).json({ error: 'Usuario sin equipo asignado' });
    }

    // Obtener equipo de la DB
    const team = db.prepare('SELECT * FROM teams WHERE id = ?').get(teamId);
    if (!team) {
      return res.status(404).json({ error: 'Equipo no encontrado' });
    }

    // Obtener miembros del equipo
    const members = db.prepare(
      'SELECT id, name, email, role FROM users WHERE team_id = ? ORDER BY name'
    ).all(teamId);

    // Calcular almacenamiento usado (listar objetos en Wasabi)
    const userFolder = team.wasabi_folder || 'CrownRFEP';
    let storageUsed = 0;
    let totalFiles = 0;

    try {
      let continuationToken = undefined;
      do {
        const command = new ListObjectsV2Command({
          Bucket: getBucket(),
          Prefix: `${userFolder}/`,
          ContinuationToken: continuationToken
        });
        const response = await getS3Client().send(command);
        if (response.Contents) {
          for (const obj of response.Contents) {
            storageUsed += obj.Size || 0;
            totalFiles++;
          }
        }
        continuationToken = response.IsTruncated ? response.NextContinuationToken : undefined;
      } while (continuationToken);
    } catch (s3Err) {
      console.warn('[Team] Error calculando almacenamiento:', s3Err.message);
    }

    const storageLimit = 100 * 1024 * 1024 * 1024; // 100 GB por defecto

    res.json({
      name: team.name,
      storageUsed,
      storageLimit,
      totalFiles,
      members: members.map(m => ({
        id: m.id,
        name: m.name,
        email: m.email,
        role: m.role
      }))
    });

  } catch (err) {
    console.error('[Team] Error obteniendo info:', err);
    res.status(500).json({ error: 'Error al obtener información del equipo' });
  }
});

// ─── GET /api/team/org-config ─────────────────────────────────────
// Lee la configuración compartida de la organización (sidebar, smart folders, etc.)
// Se almacena como org-config.json en la raíz de la carpeta Wasabi del equipo
router.get('/org-config', async (req, res) => {
  try {
    const userFolder = req.user.wasabiFolder || 'CrownRFEP';
    const key = `${userFolder}/org-config.json`;

    const command = new GetObjectCommand({
      Bucket: getBucket(),
      Key: key
    });

    const response = await getS3Client().send(command);
    const body = await response.Body.transformToString('utf-8');
    const config = JSON.parse(body);

    console.log(`[Team] org-config cargado para ${userFolder}`);
    res.json(config);

  } catch (err) {
    if (err.name === 'NoSuchKey' || err.$metadata?.httpStatusCode === 404) {
      // No existe todavía → devolver configuración vacía por defecto
      console.log(`[Team] org-config no existe, devolviendo defaults`);
      return res.json({
        version: 1,
        updatedAt: null,
        updatedBy: null,
        smartFolders: [],
        sidebarSections: {
          galleryVisible: true,
          videoLessonsVisible: true,
          trashVisible: true,
          sessionsVisible: true,
          smartFoldersVisible: true
        }
      });
    }
    console.error('[Team] Error leyendo org-config:', err);
    res.status(500).json({ error: 'Error al leer configuración de organización' });
  }
});

// ─── PUT /api/team/org-config ─────────────────────────────────────
// Guarda la configuración compartida de la organización
// Solo usuarios con rol de escritura (admin, org_admin, coach) pueden modificarla
router.put('/org-config', async (req, res) => {
  try {
    const role = req.user.role;
    const writeRoles = ['admin', 'org_admin', 'coach'];
    if (!writeRoles.includes(role)) {
      return res.status(403).json({ error: 'No tienes permisos para modificar la configuración de la organización' });
    }

    const userFolder = req.user.wasabiFolder || 'CrownRFEP';
    const key = `${userFolder}/org-config.json`;

    // Añadir metadatos de autoría
    const config = {
      ...req.body,
      version: (req.body.version || 0) + 1,
      updatedAt: new Date().toISOString(),
      updatedBy: {
        userId: req.user.userId,
        name: req.user.name,
        email: req.user.email
      }
    };

    const command = new PutObjectCommand({
      Bucket: getBucket(),
      Key: key,
      Body: JSON.stringify(config, null, 2),
      ContentType: 'application/json'
    });

    await getS3Client().send(command);

    console.log(`[Team] org-config actualizado por ${req.user.name} (${role})`);
    res.json({
      success: true,
      version: config.version,
      updatedAt: config.updatedAt
    });

  } catch (err) {
    console.error('[Team] Error guardando org-config:', err);
    res.status(500).json({ error: 'Error al guardar configuración de organización' });
  }
});

export default router;
