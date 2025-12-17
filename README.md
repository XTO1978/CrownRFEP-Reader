# CrownRFEP Reader

Aplicación de escritorio para macOS y Windows que permite leer y analizar archivos `.crown` generados por la app CrownRFEP.

## 🎯 Características

- **Importar archivos .crown**: Lee los archivos exportados desde la app móvil CrownRFEP
- **Dashboard**: Vista general con estadísticas de sesiones, videos y atletas
- **Gestión de sesiones**: Visualiza, filtra y organiza las sesiones de entrenamiento
- **Galería de videos**: Reproduce y organiza los clips de video por atleta o sección
- **Perfiles de atletas**: Información detallada de cada atleta con sus videos
- **Estadísticas**: Gráficas y análisis de datos de entrenamiento

## 🏗️ Arquitectura

La aplicación está construida con **.NET MAUI** siguiendo el patrón **MVVM**:

```
CrownRFEP-Reader/
├── Models/           # Modelos de datos (Session, Athlete, VideoClip, etc.)
├── Views/            # Páginas XAML de la UI
├── ViewModels/       # Lógica de presentación
├── Services/         # Servicios (Database, CrownFile, Statistics)
├── Converters/       # Convertidores de valores para bindings
└── Resources/        # Recursos (iconos, estilos, fuentes)
```

## 📦 Formato de archivo .crown

El archivo `.crown` es un archivo ZIP que contiene:
- `session_data.json`: Metadatos de la sesión, atletas y clips
- `videos/`: Carpeta con los archivos de video MP4
- `thumbnails/`: Carpeta con las miniaturas JPG de los videos

## 🚀 Compilar y ejecutar

### Prerrequisitos
- .NET 9 SDK
- Visual Studio 2022+ o VS Code con extensión C# Dev Kit
- Xcode (para macOS)
- Windows SDK (para Windows)

### Compilar para macOS
```bash
dotnet build -f net9.0-maccatalyst
dotnet run -f net9.0-maccatalyst
```

### Compilar para Windows
```bash
dotnet build -f net9.0-windows10.0.19041.0
dotnet run -f net9.0-windows10.0.19041.0
```

## 📱 Uso de la aplicación

1. **Importar un archivo .crown**:
   - Haz clic en "Importar archivo .crown" en el Dashboard
   - Selecciona el archivo exportado desde la app móvil
   - La aplicación extraerá los datos y videos automáticamente

2. **Navegar por las sesiones**:
   - Usa el menú lateral para acceder a Sesiones, Atletas o Estadísticas
   - Haz clic en una sesión para ver sus videos
   - Filtra videos por atleta o sección

3. **Reproducir videos**:
   - Haz clic en cualquier thumbnail para reproducir el video
   - Usa los controles de reproducción integrados

## 🗃️ Base de datos

La aplicación usa SQLite para almacenar los datos localmente. Las tablas principales son:
- `sesion`: Sesiones de entrenamiento
- `Atleta`: Información de atletas
- `videoClip`: Clips de video
- `categoria`: Categorías de atletas
- `input`: Datos de entrada durante el entrenamiento
- `valoracion`: Valoraciones de rendimiento

## 📋 Dependencias

- `Microsoft.Maui.Controls` - Framework UI multiplataforma
- `sqlite-net-pcl` - ORM para SQLite
- `CommunityToolkit.Maui` - Controles y utilidades adicionales
- `CommunityToolkit.Maui.MediaElement` - Reproductor de video

## 📄 Licencia

Este proyecto es parte del ecosistema CrownRFEP para entrenadores y atletas.

