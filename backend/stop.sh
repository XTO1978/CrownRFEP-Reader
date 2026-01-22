#!/bin/bash
# Script para detener el servidor
# Usar: ./stop.sh

cd ~/crownanalyzer-backend || exit 1

if [ -f logs/server.pid ]; then
    PID=$(cat logs/server.pid)
    if ps -p $PID > /dev/null 2>&1; then
        kill $PID
        echo "✅ Servidor detenido (PID: $PID)"
    else
        echo "ℹ️  Servidor no estaba corriendo"
    fi
    rm -f logs/server.pid
else
    echo "ℹ️  No hay archivo PID"
    pkill -f "node src/index.js" 2>/dev/null && echo "✅ Proceso node terminado" || echo "ℹ️  No había proceso"
fi
