import express from 'express';
import cors from 'cors';
import dotenv from 'dotenv';
import authRoutes from './routes/auth.js';
import filesRoutes from './routes/files.js';
import { authenticateToken } from './middleware/auth.js';

dotenv.config();

const app = express();
const PORT = process.env.PORT || 3000;

// Middleware
app.use(cors());
app.use(express.json());

// Rutas públicas
app.get('/', (req, res) => {
  res.json({ 
    name: 'CrownAnalyzer Backend',
    version: '1.0.0',
    status: 'running'
  });
});

app.get('/health', (req, res) => {
  res.json({ status: 'ok', timestamp: new Date().toISOString() });
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

app.listen(PORT, () => {
  console.log(`🚀 CrownAnalyzer Backend corriendo en puerto ${PORT}`);
  console.log(`📦 Bucket: ${process.env.WASABI_BUCKET}`);
  console.log(`🌍 Región: ${process.env.WASABI_REGION}`);
});
