# PO Time Tracker — Widget de Registro de Horas

Widget de escritorio WPF moderno que se integra con **Project Open** para registrar horas de trabajo directamente desde la bandeja del sistema de Windows. Incluye integración opcional con **Jira Cloud** y sistema de **actualizaciones automáticas** via GitHub.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue) ![WPF](https://img.shields.io/badge/UI-WPF-purple) ![Windows](https://img.shields.io/badge/OS-Windows-0078D6)

---

## Características

### PO Time Tracker
- **System Tray Widget** — Vive en la bandeja del sistema, se abre con un clic
- **Login persistente** — Credenciales encriptadas con Windows DPAPI
- **Integración con PO** — Se conecta a `registrodehoras.aspx` automáticamente
- **Navegación por fecha** — Strip semanal interactivo con resumen de horas
- **Registro rápido** — Botones de horas rápidas (0.5, 1, 2, 4, 8h)
- **UI moderna oscura** — Diseño tipo Fluent/WinUI con animaciones
- **Fallback local** — Si el servidor no responde, guarda localmente
- **Log de errores** — Registro automático en archivos con rotación de 7 días
- **Inicio automático** — Se registra en el inicio de Windows automáticamente

### Jira Integration
- **Widget flotante independiente** — Mismo estilo visual que el widget de PO
- **Autenticación segura** — API token encriptado con DPAPI
- **Mis issues** — Lista los issues asignados, filtrables por proyecto y estado
- **Búsqueda** — Por clave exacta (`PROJ-123`) o texto libre vía JQL
- **Registro de worklogs** — Carga horas directamente en Jira
- **Doble registro** — Desde el formulario de PO, registrá en ambos sistemas con un solo clic

### Control de Versiones y Actualizaciones Automáticas
- **Versión visible** — La versión actual aparece en el pie de ambas ventanas
- **Auto-update** — Al iniciar, la app consulta GitHub para detectar nuevas versiones
- **Un clic para actualizar** — Si hay una versión nueva, un aviso en el pie permite descargar e instalar sin salir de la app

---

## Requisitos

- **Windows 10/11**
- **.NET 8 SDK** — [Descargar](https://dotnet.microsoft.com/download/dotnet/8.0) *(solo para compilar — el .exe de release ya lo incluye)*
- **Visual Studio 2022** *(opcional, solo si vas a modificar el código)*

---

## Instalación rápida (usuarios finales)

1. Ir a la sección **[Releases](https://github.com/NicolasPecoy/POTimeTracker/releases/latest)**
2. Descargar `POTimeTracker-X.Y.Z.exe`
3. Copiar el `.exe` a cualquier carpeta (Escritorio, Documentos, etc.)
4. Ejecutar — no requiere instalación, no requiere .NET instalado

> La app detecta automáticamente si hay una versión más nueva al iniciar.

---

## Uso diario

**Solo PO:**
- Click en el ícono del tray → seleccionar proyecto → tarea → horas → "Registrar Horas"

**PO + Jira simultáneo:**
- En el formulario de PO, tildar **"Registrar también en Jira"**
- Escribir la clave del issue (ej. `PROJ-123`)
- Click en "Registrar Horas" → las horas se envían a PO y Jira en un solo paso

**Solo Jira:**
- Abrir el widget de Jira (botón **J** del header o menú del tray)
- Seleccionar un issue → ingresar horas y notas → "Registrar en Jira"

---

## Configuración de Jira

1. Abrir el widget de Jira (botón **J** en el header)
2. Completar los campos:
   - **URL de Jira**: `https://tu-empresa.atlassian.net`
   - **Email**: tu email de cuenta Atlassian
   - **API Token**: generarlo en [Atlassian Account → Security → API tokens](https://id.atlassian.com/manage-profile/security/api-tokens)
   - **Proyecto por defecto** *(opcional)*: clave del proyecto, ej. `PROJ`
3. Click en **Conectar a Jira**

El API token se guarda encriptado con DPAPI — solo tu usuario de Windows puede leerlo.

---

## Guía de Control de Versiones y Releases

> Esta sección explica desde cero cómo funciona el sistema de versiones y cómo publicar una nueva versión del programa.

### ¿Qué es una versión?

Una versión es un número con formato `X.Y.Z` (por ejemplo `1.0.0`, `1.2.3`, `2.0.0`):

- **X (Major)** — Cambio grande o incompatible (ej: rediseño completo)
- **Y (Minor)** — Feature nueva (ej: nueva integración, nueva pantalla)
- **Z (Patch)** — Corrección de bug pequeño

### ¿Dónde vive la versión?

La versión está definida en una sola línea del archivo `POTimeTracker.csproj`:

```xml
<Version>1.0.0</Version>
```

Todo lo demás (la pantalla, el exe generado, el release de GitHub) la lee de ahí.

---

### Paso a Paso: Publicar una nueva versión

#### Paso 1 — Hacer los cambios al código

Modificar el código como siempre. Cuando estés satisfecho con los cambios, continuar al siguiente paso.

#### Paso 2 — Actualizar la versión en el .csproj

Abrir `POTimeTracker.csproj` y cambiar la línea de versión:

```xml
<!-- Antes -->
<Version>1.0.0</Version>

<!-- Después (ejemplo: agregaste una feature nueva) -->
<Version>1.1.0</Version>
```

> **Regla simple:**
> - Bug fix → incrementá el tercer número: `1.0.0` → `1.0.1`
> - Feature nueva → incrementá el segundo número: `1.0.0` → `1.1.0`
> - Cambio grande → incrementá el primero: `1.0.0` → `2.0.0`

#### Paso 3 — Commitear los cambios

En la terminal (PowerShell o CMD):

```powershell
git add .
git commit -m "Versión 1.1.0 — descripción breve de los cambios"
```

#### Paso 4 — Crear un tag de Git

Un **tag** es una marca en el historial de Git que identifica el punto exacto donde fue cada versión. El tag **debe tener el mismo número** que pusiste en el `.csproj`:

```powershell
git tag v1.1.0
```

#### Paso 5 — Subir el tag a GitHub

```powershell
git push origin master
git push origin v1.1.0
```

> Esto es lo que **dispara automáticamente** la construcción del ejecutable en GitHub.

#### Paso 6 — Esperar que GitHub Actions construya el ejecutable

GitHub tiene un servidor propio que, al detectar el tag, hace lo siguiente sin que tengas que hacer nada:

1. Descarga el código fuente
2. Verifica que el número de versión en el `.csproj` coincida con el tag
3. Compila el proyecto y genera un `.exe` que incluye todo (el runtime de .NET incluido)
4. Crea un **Release** público en GitHub con el `.exe` adjunto

Podés ver el progreso en:
`https://github.com/NicolasPecoy/POTimeTracker/actions`

Si todo sale bien, en unos minutos aparece el release en:
`https://github.com/NicolasPecoy/POTimeTracker/releases`

#### Paso 7 — Los usuarios reciben la actualización

La próxima vez que alguien abra la app, verá en el pie de pantalla:

```
v1.0.0 - Widget de registro  ★ v1.1.0 disponible
```

Al hacer **click** en ese texto:
- Si el `.exe` está adjunto al release: lo descarga automáticamente, reemplaza el exe y reinicia la app
- Si no hay `.exe` adjunto: abre el navegador en la página de releases para que lo descargue manualmente

---

### Verificar que el número es correcto (el workflow lo chequea)

El workflow de GitHub Actions tiene un paso que **falla el build** si el tag no coincide con el `.csproj`. Por ejemplo, si pusiste `v1.2.0` como tag pero el `.csproj` dice `1.1.0`, el build falla con:

```
MISMATCH: .csproj dice '1.1.0' pero el tag dice '1.2.0'.
Actualizá <Version> en el .csproj antes de taggear.
```

Esto evita publicar accidentalmente un exe con la versión incorrecta.

---

### Resumen rápido (cheat sheet)

```powershell
# 1. Editar POTimeTracker.csproj → cambiar <Version>X.Y.Z</Version>

# 2. Commitear
git add POTimeTracker.csproj
git commit -m "Bump version to X.Y.Z"

# 3. Taggear y pushear
git push origin master
git tag vX.Y.Z
git push origin vX.Y.Z

# Listo — GitHub hace el resto automáticamente
```

---

## Build manual (para desarrolladores)

### Compilar y ejecutar en modo desarrollo

```powershell
cd POTimeTracker
dotnet restore
dotnet run
```

### Generar ejecutable self-contained manualmente

```powershell
dotnet publish POTimeTracker.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o publish/
```

El `.exe` queda en `publish\POTimeTracker.exe`.

### En Visual Studio 2022

1. Abrir `POTimeTracker.csproj`
2. Click derecho → **Restore NuGet Packages**
3. Presionar **F5** para compilar y ejecutar

---

## Estructura del Proyecto

```
POTimeTracker/
├── .github/
│   └── workflows/
│       └── release.yml             # Build automático y publicación en GitHub
│
├── POTimeTracker.csproj            # Proyecto .NET 8 WPF (contiene <Version>)
├── App.xaml / App.xaml.cs          # Entry point y manejo de errores globales
│
├── Themes/
│   └── DarkTheme.xaml             # Tema oscuro (colores, estilos, control templates)
│
├── Views/
│   ├── MainWindow.xaml/.cs        # Widget principal de PO (muestra versión en footer)
│   ├── JiraWindow.xaml/.cs        # Widget de integración Jira (muestra versión en footer)
│   ├── SettingsWindow.xaml/.cs    # Ventana de configuración
│   └── ReminderWindow.xaml/.cs    # Recordatorio diario de horas
│
├── Models/
│   ├── Models.cs                  # POProject, POTask, TimeEntry, LoginCredentials
│   └── JiraModels.cs              # JiraConfig, JiraProject, JiraIssue
│
├── Services/
│   ├── POApiService.cs            # Cliente HTTP para PO
│   ├── JiraApiService.cs          # Cliente HTTP para Jira REST API v3
│   ├── JiraConfigService.cs       # Almacenamiento seguro de config Jira (DPAPI)
│   ├── CredentialService.cs       # Credenciales PO y entradas locales (DPAPI)
│   ├── UpdateService.cs           # Detección y descarga de actualizaciones de GitHub
│   └── LogService.cs              # Logger con rotación de archivos diaria
│
└── Assets/
    └── icon.ico                   # Ícono de la aplicación
```

---

## Datos almacenados localmente

Todos los archivos se guardan en `%LOCALAPPDATA%\POTimeTracker\`:

| Archivo | Contenido |
|---------|-----------|
| `cred.dat` | Credenciales PO cifradas (DPAPI) |
| `config.json` | Configuración del widget (recordatorio, objetivo semanal, etc.) |
| `entries.json` | Registros de horas locales (últimos 3 meses) |
| `jira_config.json` | Configuración de Jira (URL, email, proyecto por defecto) |
| `jira_token.dat` | API token de Jira cifrado (DPAPI) |
| `logs/app-YYYY-MM-DD.log` | Logs de la aplicación (7 días de retención) |

---

## Cómo funciona la integración con PO

El widget se comunica con Project Open de la misma forma que un navegador:

1. **Login**: GET `sgplogin.aspx` → extrae `__VIEWSTATE` y campos GeneXus → POST con credenciales
2. **Proyectos/Tareas**: GET `registrodehoras.aspx` → parsea grids GeneXus
3. **Registro**: GET fresh page state → localiza la celda exacta del grid → POST con evento `CONFIRMAR`

La sesión se renueva automáticamente cada N horas (configurable, default 3h).

---

## Cómo funciona la integración con Jira

Usa la **Jira Cloud REST API v3** con autenticación Basic (email + API token):

| Operación | Endpoint |
|-----------|----------|
| Verificar conexión | `GET /rest/api/3/myself` |
| Listar proyectos | `GET /rest/api/3/project/search` |
| Buscar issues (JQL) | `GET /rest/api/3/search?jql=...` |
| Registrar worklog | `POST /rest/api/3/issue/{key}/worklog` |

---

## Cómo funciona el sistema de actualizaciones

Al iniciar la app (5 segundos después para no bloquear la UI), el servicio de actualización:

1. Consulta `https://api.github.com/repos/NicolasPecoy/POTimeTracker/releases/latest`
2. Compara el número de versión del release con el del exe que está corriendo
3. Si hay una versión más nueva:
   - El texto del footer cambia a amarillo con el aviso
   - Al hacer click, descarga el `.exe` nuevo a una carpeta temporal
   - Lanza un script que espera a que la app cierre, reemplaza el `.exe` y la reinicia
4. Si ya tenés la última versión, muestra un mensaje confirmándolo

---

## Seguridad

- Las credenciales de PO y el API token de Jira se cifran con **Windows DPAPI**
- Solo el usuario de Windows que los guardó puede descifrarlos
- No se transmiten datos a ningún servidor externo salvo PO y Jira
- Las actualizaciones se descargan directamente de GitHub (HTTPS)

---

## Dependencias NuGet

| Paquete | Versión | Uso |
|---------|---------|-----|
| `Hardcodet.NotifyIcon.Wpf` | 1.1.0 | Ícono en la bandeja del sistema |
| `System.Security.Cryptography.ProtectedData` | 8.0.0 | Cifrado DPAPI |
