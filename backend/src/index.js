import express from 'express';
import cors from 'cors';
import dotenv from 'dotenv';
import authRoutes from './routes/auth.js';
import filesRoutes from './routes/files.js';
import adminRoutes from './routes/admin.js';
import { authenticateToken, requireAdmin } from './middleware/auth.js';
import path from 'path';
import { fileURLToPath } from 'url';
import bcrypt from 'bcryptjs';
import db from './db/database.js';

dotenv.config();

const app = express();
const PORT = process.env.PORT || 3000;
const IS_PRODUCTION = process.env.NODE_ENV === 'production';

// ============================================
// Auto-shutdown después de 120 minutos de inactividad
// SOLO en desarrollo local - en producción PM2 mantiene el proceso
// ============================================
const INACTIVITY_TIMEOUT_MS = 120 * 60 * 1000; // 120 minutos
let lastActivityTime = Date.now();
let shutdownTimer = null;

function resetInactivityTimer() {
  // No auto-shutdown en producción
  if (IS_PRODUCTION) return;
  
  lastActivityTime = Date.now();
  
  if (shutdownTimer) {
    clearTimeout(shutdownTimer);
  }
  
  shutdownTimer = setTimeout(() => {
    const inactiveMinutes = Math.round((Date.now() - lastActivityTime) / 60000);
    console.log(`\n⏰ Auto-shutdown: ${inactiveMinutes} minutos de inactividad`);
    console.log('👋 Cerrando servidor backend...');
    process.exit(0);
  }, INACTIVITY_TIMEOUT_MS);
}

// Middleware para rastrear actividad
function trackActivity(req, res, next) {
  lastActivityTime = Date.now();
  resetInactivityTimer();
  next();
}

// Middleware
app.use(cors());
app.use(express.json());
app.use(trackActivity); // Rastrear cada petición

// Crear admin por env si no existe
const adminEmail = process.env.ADMIN_EMAIL;
const adminPassword = process.env.ADMIN_PASSWORD;
const adminName = process.env.ADMIN_NAME || 'Administrador';
const adminTeamId = process.env.ADMIN_TEAM_ID || 'rfep';

if (adminEmail && adminPassword) {
  try {
    const existing = db.prepare('SELECT id FROM users WHERE email = ?').get(adminEmail);
    if (!existing) {
      const passwordHash = await bcrypt.hash(adminPassword, 12);
      db.prepare('INSERT INTO users (email, password_hash, name, role, team_id) VALUES (?, ?, ?, ?, ?)')
        .run(adminEmail, passwordHash, adminName, 'admin', adminTeamId);
      console.log(`[Admin] Usuario admin creado: ${adminEmail}`);
    }
  } catch (err) {
    console.error('[Admin] Error creando admin:', err);
  }
}

// Admin UI estática
const __dirname = path.dirname(fileURLToPath(import.meta.url));
app.use('/admin', express.static(path.join(__dirname, 'admin')));

// Rutas públicas
app.get('/', (req, res) => {
  res.json({ 
    name: 'CrownAnalyzer Backend',
    version: '1.0.0',
    status: 'running',
    uptime: Math.round(process.uptime()),
    lastActivity: new Date(lastActivityTime).toISOString()
  });
});

app.get('/health', (req, res) => {
  const response = { 
    status: 'ok', 
    timestamp: new Date().toISOString(),
    uptimeSeconds: Math.round(process.uptime()),
    environment: IS_PRODUCTION ? 'production' : 'development',
    inactiveMinutes: Math.round((Date.now() - lastActivityTime) / 60000)
  };
  
  // Solo mostrar info de auto-shutdown en desarrollo
  if (!IS_PRODUCTION) {
    response.autoShutdownInMinutes = Math.round((INACTIVITY_TIMEOUT_MS - (Date.now() - lastActivityTime)) / 60000);
  }
  
  res.json(response);
});

// Rutas de autenticación (públicas)
app.use('/api/auth', authRoutes);

// Rutas de archivos (protegidas)
app.use('/api/files', authenticateToken, filesRoutes);

// Rutas de administración (solo admin)
app.use('/api/admin', authenticateToken, requireAdmin, adminRoutes);

// Manejo de errores
app.use((err, req, res, next) => {
  console.error('[Error]', err);
  res.status(500).json({ error: 'Error interno del servidor' });
});

// Escuchar en 0.0.0.0 para aceptar conexiones desde otros dispositivos (iPad, etc.)
app.listen(PORT, '0.0.0.0', () => {
  console.log(`🚀 CrownAnalyzer Backend corriendo en puerto ${PORT}`);
  console.log(`🌐 Escuchando en 0.0.0.0:${PORT} (accesible desde la red local)`);
  console.log(`📦 Bucket: ${process.env.WASABI_BUCKET}`);
  console.log(`🌍 Región: ${process.env.WASABI_REGION}`);
  console.log(`🔧 Entorno: ${IS_PRODUCTION ? 'PRODUCCIÓN' : 'desarrollo'}`);
  
  if (!IS_PRODUCTION) {
    console.log(`⏰ Auto-shutdown: ${INACTIVITY_TIMEOUT_MS / 60000} minutos de inactividad`);
    // Iniciar el temporizador de inactividad solo en desarrollo
    resetInactivityTimer();
  } else {
    console.log(`✅ Modo producción: sin auto-shutdown`);
  }
});
