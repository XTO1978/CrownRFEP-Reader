#!/bin/bash

# ============================================
# Script de Deploy para CrownAnalyzer Backend
# Servidor: IONOS VPS
# ============================================

set -e  # Salir si hay errores

echo "🚀 Iniciando deploy de CrownAnalyzer Backend..."

# Colores para output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Directorio del proyecto (ajustar según tu servidor)
PROJECT_DIR="${PROJECT_DIR:-/home/crown/crownanalyzer-backend}"

# 1. Ir al directorio del proyecto
echo -e "${YELLOW}📂 Cambiando a directorio del proyecto...${NC}"
cd "$PROJECT_DIR"

# 2. Obtener últimos cambios
echo -e "${YELLOW}📥 Obteniendo últimos cambios de Git...${NC}"
git pull origin main

# 3. Instalar dependencias
echo -e "${YELLOW}📦 Instalando dependencias...${NC}"
npm ci --production

# 4. Crear directorio de logs si no existe
mkdir -p logs

# 5. Reiniciar con PM2
echo -e "${YELLOW}🔄 Reiniciando servicio con PM2...${NC}"
pm2 restart ecosystem.config.cjs --env production || pm2 start ecosystem.config.cjs --env production

# 6. Guardar configuración de PM2
pm2 save

echo -e "${GREEN}✅ Deploy completado exitosamente!${NC}"
echo ""
echo "📊 Estado del servicio:"
pm2 status crownanalyzer-backend
