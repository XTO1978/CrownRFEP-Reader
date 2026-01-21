# CrownAnalyzer Backend

Backend mínimo para gestión segura de archivos en Wasabi. Las credenciales de Wasabi **nunca** salen del servidor.

## 🚀 Inicio Rápido

### 1. Instalar dependencias

```bash
cd backend
npm install
```

### 2. Configurar variables de entorno

```bash
cp .env.example .env
# Editar .env con tus valores
```

### 3. Inicializar base de datos

```bash
npm run init-db
```

Esto crea un usuario admin por defecto:
- **Email:** `admin@rfep.es`
- **Password:** `Crown2026!`

### 4. Ejecutar servidor

```bash
# Desarrollo (con hot reload)
npm run dev

# Producción
npm start
```

El servidor estará en `http://localhost:3000`

## 📡 API Endpoints

### Autenticación

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| POST | `/api/auth/register` | Registrar nuevo usuario |
| POST | `/api/auth/login` | Iniciar sesión |
| POST | `/api/auth/refresh` | Refrescar token |
| GET | `/api/auth/me` | Obtener usuario actual |

### Archivos (requiere autenticación)

| Método | Endpoint | Descripción |
|--------|----------|-------------|
| GET | `/api/files/list` | Listar archivos |
| POST | `/api/files/sign-upload` | Obtener URL firmada para subir |
| POST | `/api/files/sign-download` | Obtener URL firmada para descargar |
| DELETE | `/api/files/:key` | Eliminar archivo |
| GET | `/api/files/info/:key` | Info de un archivo |

## 🔐 Autenticación

El cliente obtiene un JWT token en el login y lo envía en el header:

```
Authorization: Bearer <token>
```

## 📦 Despliegue

### Railway

1. Conecta el repo a Railway
2. Configura las variables de entorno en Railway
3. Deploy automático

### Render

1. Crea un nuevo Web Service
2. Conecta el repo
3. Build Command: `npm install`
4. Start Command: `npm start`
5. Configura variables de entorno

### Docker

```dockerfile
FROM node:20-alpine
WORKDIR /app
COPY package*.json ./
RUN npm ci --only=production
COPY . .
EXPOSE 3000
CMD ["npm", "start"]
```

## 🛡️ Seguridad

- Las credenciales de Wasabi **solo** están en el servidor
- Los tokens JWT expiran en 7 días
- Las URLs firmadas expiran en 1 hora
- Cada usuario solo puede acceder a la carpeta de su equipo
