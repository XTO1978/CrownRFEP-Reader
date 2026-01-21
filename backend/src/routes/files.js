import express from 'express';
import { S3Client, ListObjectsV2Command, DeleteObjectCommand, HeadObjectCommand } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import { PutObjectCommand, GetObjectCommand } from '@aws-sdk/client-s3';

const router = express.Router();

// Cliente S3 lazy - se inicializa en la primera petición
let s3Client = null;

function getS3Client() {
  if (!s3Client) {
    console.log('[S3] Inicializando cliente con región:', process.env.WASABI_REGION);
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

// GET /api/files/list - Listar archivos del equipo
router.get('/list', async (req, res) => {
  try {
    const { prefix = '', maxKeys = 1000 } = req.query;
    const userFolder = req.user.wasabiFolder || 'CrownRFEP';
    
    // Construir el prefijo completo
    const fullPrefix = prefix ? `${userFolder}/${prefix}` : `${userFolder}/`;

    const command = new ListObjectsV2Command({
      Bucket: getBucket(),
      Prefix: fullPrefix,
      MaxKeys: parseInt(maxKeys)
    });

    const response = await getS3Client().send(command);

    // Transformar la respuesta para el cliente
    const files = (response.Contents || []).map(item => ({
      key: item.Key.replace(`${userFolder}/`, ''), // Quitar el prefijo del equipo
      fullKey: item.Key,
      size: item.Size,
      lastModified: item.LastModified,
      isFolder: item.Key.endsWith('/')
    }));

    res.json({
      success: true,
      files,
      totalCount: files.length,
      isTruncated: response.IsTruncated
    });

  } catch (err) {
    console.error('[Files] Error listando archivos:', err);
    res.status(500).json({ error: 'Error al listar archivos' });
  }
});

// POST /api/files/sign-upload - Obtener URL firmada para subir (formato legacy)
router.post('/sign-upload', async (req, res) => {
  try {
    const { fileName, contentType, folder = '' } = req.body;

    if (!fileName) {
      return res.status(400).json({ error: 'Nombre de archivo requerido' });
    }

    const userFolder = req.user.wasabiFolder || 'CrownRFEP';
    const key = folder ? `${userFolder}/${folder}/${fileName}` : `${userFolder}/${fileName}`;

    const command = new PutObjectCommand({
      Bucket: getBucket(),
      Key: key,
      ContentType: contentType || 'application/octet-stream'
    });

    // URL válida por 1 hora
    const signedUrl = await getSignedUrl(getS3Client(), command, { expiresIn: 3600 });

    console.log(`[Files] URL de subida generada: ${key}`);

    res.json({
      success: true,
      uploadUrl: signedUrl,
      key: key.replace(`${userFolder}/`, ''),
      expiresIn: 3600
    });

  } catch (err) {
    console.error('[Files] Error generando URL de subida:', err);
    res.status(500).json({ error: 'Error al generar URL de subida' });
  }
});

// POST /api/files/upload-url - Obtener URL firmada para subir (formato nuevo usado por SyncService)
router.post('/upload-url', async (req, res) => {
  try {
    const { path, contentType, expirationMinutes = 60 } = req.body;

    if (!path) {
      return res.status(400).json({ error: 'Path del archivo requerido' });
    }

    const userFolder = req.user.wasabiFolder || 'CrownRFEP';
    // El path ya viene en formato "sessions/{id}/videos/{id}.mp4"
    const key = `${userFolder}/${path}`;

    const command = new PutObjectCommand({
      Bucket: getBucket(),
      Key: key,
      ContentType: contentType || 'application/octet-stream'
    });

    const expiresIn = Math.min(expirationMinutes * 60, 7200); // Máximo 2 horas
    const signedUrl = await getSignedUrl(getS3Client(), command, { expiresIn });

    console.log(`[Files] URL de subida generada para: ${key}`);

    res.json({
      success: true,
      url: signedUrl,
      key: path,
      fullKey: key,
      expiresIn
    });

  } catch (err) {
    console.error('[Files] Error generando URL de subida:', err);
    res.status(500).json({ error: 'Error al generar URL de subida' });
  }
});

// POST /api/files/download-url - Obtener URL firmada para descargar (formato nuevo usado por SyncService)
router.post('/download-url', async (req, res) => {
  try {
    const { path, expirationMinutes = 60 } = req.body;

    if (!path) {
      return res.status(400).json({ error: 'Path del archivo requerido' });
    }

    const userFolder = req.user.wasabiFolder || 'CrownRFEP';
    const key = `${userFolder}/${path}`;

    const command = new GetObjectCommand({
      Bucket: getBucket(),
      Key: key
    });

    const expiresIn = Math.min(expirationMinutes * 60, 7200);
    const signedUrl = await getSignedUrl(getS3Client(), command, { expiresIn });

    console.log(`[Files] URL de descarga generada para: ${key}`);

    res.json({
      success: true,
      url: signedUrl,
      key: path,
      fullKey: key,
      expiresIn
    });

  } catch (err) {
    console.error('[Files] Error generando URL de descarga:', err);
    res.status(500).json({ error: 'Error al generar URL de descarga' });
  }
});

// POST /api/files/sign-download - Obtener URL firmada para descargar
router.post('/sign-download', async (req, res) => {
  try {
    const { key } = req.body;

    if (!key) {
      return res.status(400).json({ error: 'Key del archivo requerida' });
    }

    const userFolder = req.user.wasabiFolder || 'CrownRFEP';
    const fullKey = key.startsWith(userFolder) ? key : `${userFolder}/${key}`;

    const command = new GetObjectCommand({
      Bucket: getBucket(),
      Key: fullKey
    });

    // URL válida por 1 hora
    const signedUrl = await getSignedUrl(getS3Client(), command, { expiresIn: 3600 });

    console.log(`[Files] URL de descarga generada: ${fullKey}`);

    res.json({
      success: true,
      downloadUrl: signedUrl,
      expiresIn: 3600
    });

  } catch (err) {
    console.error('[Files] Error generando URL de descarga:', err);
    res.status(500).json({ error: 'Error al generar URL de descarga' });
  }
});

// DELETE /api/files/:key - Eliminar archivo
router.delete('/:key(*)', async (req, res) => {
  try {
    const { key } = req.params;

    if (!key) {
      return res.status(400).json({ error: 'Key del archivo requerida' });
    }

    const userFolder = req.user.wasabiFolder || 'CrownRFEP';
    const fullKey = key.startsWith(userFolder) ? key : `${userFolder}/${key}`;

    const command = new DeleteObjectCommand({
      Bucket: getBucket(),
      Key: fullKey
    });

    await getS3Client().send(command);

    console.log(`[Files] Archivo eliminado: ${fullKey}`);

    res.json({
      success: true,
      message: 'Archivo eliminado'
    });

  } catch (err) {
    console.error('[Files] Error eliminando archivo:', err);
    res.status(500).json({ error: 'Error al eliminar archivo' });
  }
});

// GET /api/files/info/:key - Obtener información de un archivo
router.get('/info/:key(*)', async (req, res) => {
  try {
    const { key } = req.params;

    const userFolder = req.user.wasabiFolder || 'CrownRFEP';
    const fullKey = key.startsWith(userFolder) ? key : `${userFolder}/${key}`;

    const command = new HeadObjectCommand({
      Bucket: getBucket(),
      Key: fullKey
    });

    const response = await getS3Client().send(command);

    res.json({
      success: true,
      file: {
        key: key,
        size: response.ContentLength,
        contentType: response.ContentType,
        lastModified: response.LastModified,
        metadata: response.Metadata
      }
    });

  } catch (err) {
    if (err.name === 'NotFound') {
      return res.status(404).json({ error: 'Archivo no encontrado' });
    }
    console.error('[Files] Error obteniendo info:', err);
    res.status(500).json({ error: 'Error al obtener información del archivo' });
  }
});

export default router;
