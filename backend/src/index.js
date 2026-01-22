import express from 'express';
import cors from 'cors';
import dotenv from 'dotenv';
import authRoutes from './routes/auth.js';
import filesRoutes from './routes/files.js';
import { authenticateToken } from './middleware/auth.js';

dotenv.config();

const app = express();
const PORT = process.env.PORT || 3000;

// ============================================
// Auto-shutdown después de 120 minutos de inactividad
// ============================================
const INACTIVITY_TIMEOUT_MS = 120 * 60 * 1000; // 120 minutos
let lastActivityTime = Date.now();
let shutdownTimer = null;

function resetInactivityTimer() {
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
  resetInactivityTimer();
  next();
}

// Middleware
app.use(cors());
app.use(express.json());
app.use(trackActivity); // Rastrear cada petición

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
  res.json({ 
    status: 'ok', 
    timestamp: new Date().toISOString(),
    uptimeSeconds: Math.round(process.uptime()),
    inactiveMinutes: Math.round((Date.now() - lastActivityTime) / 60000),
    autoShutdownInMinutes: Math.round((INACTIVITY_TIMEOUT_MS - (Date.now() - lastActivityTime)) / 60000)
  });
});

// Rutas de autenticación (públicas)
app.use('/api/auth', authRoutes);

// Rutas de archivos (protegidas)
app.use('/api/files', authenticateToken, filesRoutes);

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
  console.log(`⏰ Auto-shutdown: ${INACTIVITY_TIMEOUT_MS / 60000} minutos de inactividad`);
  
  // Iniciar el temporizador de inactividad
  resetInactivityTimer();
});
