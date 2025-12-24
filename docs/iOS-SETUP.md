# Configuración para iOS/iPad

## Requisitos previos

1. **Xcode** instalado desde la Mac App Store
2. **Command Line Tools** de Xcode: `xcode-select --install`
3. **.NET 9 SDK** con workload de iOS: `dotnet workload install ios`
4. **Cuenta de desarrollador de Apple** (gratuita para desarrollo local, de pago para App Store)

## Configuración del aprovisionamiento (Signing)

### Opción 1: Aprovisionamiento automático (desarrollo)

1. Abre Xcode
2. Ve a **Xcode > Settings > Accounts**
3. Añade tu Apple ID
4. Selecciona tu equipo de desarrollo

El aprovisionamiento automático creará los certificados y perfiles necesarios.

### Opción 2: Aprovisionamiento manual

1. Ve a [Apple Developer Portal](https://developer.apple.com)
2. Crea un App ID con el bundle ID: `com.companyname.crownrfepreader`
3. Habilita las capacidades necesarias:
   - HealthKit
   - App Groups (si es necesario)
4. Crea un perfil de aprovisionamiento para desarrollo
5. Descarga e instala el perfil

## Tareas disponibles en VS Code

Abre la paleta de comandos (`Cmd+Shift+P`) y ejecuta `Tasks: Run Task`:

| Tarea | Descripción |
|-------|-------------|
| `iOS: Build for Device` | Compila para dispositivo físico |
| `iOS: Build for Simulator` | Compila para simulador |
| `iOS: Run on iPad Simulator` | Ejecuta en simulador de iPad |
| `iOS: Run on Connected Device` | Ejecuta en dispositivo conectado |
| `iOS: Run with Hot Reload (Simulator)` | Ejecuta con Hot Reload en simulador |
| `iOS: List Available Simulators` | Lista los simuladores disponibles |
| `iOS: Boot iPad Pro Simulator` | Inicia el simulador de iPad Pro |
| `iOS: Open Simulator App` | Abre la aplicación Simulator |
| `iOS: Deploy to iPad (Full)` | Flujo completo: inicia simulador y despliega |

## Ejecutar en simulador de iPad

1. Ejecuta la tarea `iOS: Deploy to iPad (Full)` para el flujo completo, o:

2. Manualmente:
   ```bash
   # Listar simuladores disponibles
   xcrun simctl list devices available | grep iPad
   
   # Iniciar un simulador específico
   xcrun simctl boot "iPad Pro 13-inch (M4)"
   
   # Abrir la app Simulator
   open -a Simulator
   
   # Compilar y ejecutar
   dotnet build -f net9.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64 -t:Run
   ```

## Ejecutar en iPad físico

### Preparación del iPad

1. Conecta el iPad al Mac mediante USB
2. En el iPad: **Ajustes > Privacidad y seguridad > Modo de desarrollador** → Activar
3. Confía en tu Mac cuando se te solicite

### Desde VS Code

1. Ejecuta la tarea `iOS: Run on Connected Device`

### Desde terminal

```bash
# Compilar y ejecutar en dispositivo conectado
dotnet build -f net9.0-ios -c Debug -t:Run
```

## Solución de problemas

### Error: "No signing identity found"

1. Abre el proyecto en Xcode (genera un .xcodeproj temporal si es necesario)
2. Configura el equipo de desarrollo en la pestaña "Signing & Capabilities"
3. O añade las propiedades en el .csproj:
   ```xml
   <PropertyGroup Condition="$(TargetFramework.Contains('-ios'))">
       <CodesignKey>Apple Development</CodesignKey>
       <CodesignProvision>Automatic</CodesignProvision>
   </PropertyGroup>
   ```

### Error: "Device is locked"

Desbloquea el iPad y confía en el Mac si es la primera conexión.

### Error: "Unable to install app"

1. Elimina cualquier versión anterior de la app del iPad
2. Reinicia el iPad
3. Verifica que el perfil de aprovisionamiento incluya el dispositivo

### El simulador no encuentra la app

```bash
# Limpiar y recompilar
dotnet clean
dotnet build -f net9.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-arm64 -t:Run
```

## Diferencias con MacCatalyst

| Característica | MacCatalyst | iOS/iPad |
|----------------|-------------|----------|
| HealthKit | ❌ No disponible | ✅ Disponible |
| Sandbox de archivos | Más flexible | Más restrictivo |
| Multitarea | Ventanas libres | Split View / Slide Over |
| Entrada | Ratón/trackpad + teclado | Táctil + Apple Pencil |

## Próximos pasos

1. [ ] Adaptar la UI para pantalla táctil
2. [ ] Optimizar para Split View
3. [ ] Probar HealthKit con datos reales
4. [ ] Configurar aprovisionamiento para App Store
