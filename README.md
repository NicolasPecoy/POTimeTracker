# PO Time Tracker — Widget de Registro de Horas

Widget de escritorio WPF moderno que se integra con **Project Open** para registrar horas de trabajo directamente desde la bandeja del sistema de Windows. Incluye un módulo opcional de integración con **Jira Cloud** para registrar worklogs en paralelo.

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

### Jira Integration (nuevo)
- **Widget flotante independiente** — Mismo estilo visual que el widget de PO
- **Autenticación segura** — API token encriptado con DPAPI
- **Mis issues** — Lista los issues de Jira asignados al usuario, filtrables por proyecto
- **Búsqueda** — Por clave exacta (ej. `PROJ-123`) o texto libre vía JQL
- **Registro de worklogs** — Carga horas directamente en Jira con fecha y comentario
- **Doble registro** — Desde el formulario de PO, opción para registrar en ambos sistemas al mismo tiempo con un solo clic

---

## Requisitos

- **Windows 10/11**
- **.NET 8 SDK** — [Descargar](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Visual Studio 2022** (recomendado) o VS Code con C# extension

---

## Instalación y Build

### Opción 1: Visual Studio
```
1. Abrir POTimeTracker.csproj en Visual Studio 2022
2. Click derecho → "Restore NuGet Packages"
3. Presionar F5 para compilar y ejecutar
```

> **Nota:** Si los archivos de la integración Jira no aparecen en el Solution Explorer,
> hacer click derecho en el proyecto → **Reload Project** (o cerrar y volver a abrir VS).
> El SDK los incluye automáticamente, pero VS necesita refrescar cuando se agregan archivos externamente.

### Opción 2: Línea de comandos
```bash
cd POTimeTracker
dotnet restore
dotnet build
dotnet run
```

### Crear ejecutable publicable
```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
El `.exe` se genera en `bin/Release/net8.0-windows/win-x64/publish/`

---

## Configuración

### PO Time Tracker — Primera ejecución
1. Ejecutar `POTimeTracker.exe`
2. Ingresar en la ventana de login:
   - **Servidor**: `http://po.invenzis.com:8080` (preconfigurado)
   - **Usuario** y **Contraseña** de PO
3. Marcar "Recordar sesión" para auto-login

### Jira Integration — Configuración
1. Desde el widget de PO, hacer click en el botón **J** del header (o "Abrir Jira" en el tray)
2. Completar los campos:
   - **URL de Jira**: `https://tu-empresa.atlassian.net`
   - **Email**: tu email de cuenta Atlassian
   - **API Token**: generarlo en [Atlassian Account → Security → API tokens](https://id.atlassian.com/manage-profile/security/api-tokens)
   - **Proyecto por defecto** (opcional): clave del proyecto, ej. `PROJ`
3. Click en **Conectar a Jira**

El API token se guarda encriptado con DPAPI, igual que las credenciales de PO.

### Uso diario

**Solo PO:**
- Click en el ícono del tray → seleccionar proyecto → tarea → horas → "Registrar Horas"

**PO + Jira simultáneo:**
- En el formulario de PO, tildar **"Registrar también en Jira"**
- Escribir la clave del issue (ej. `PROJ-123`) — se valida automáticamente
- Click en "Registrar Horas" → las horas se envían a PO y a Jira en un solo paso

**Solo Jira:**
- Abrir el widget de Jira (botón J o tray)
- Seleccionar un issue de la lista → ingresar horas y notas → "Registrar en Jira"

---

## Estructura del Proyecto

```
POTimeTracker/
├── App.xaml / App.xaml.cs          # Entry point y manejo de errores globales
├── POTimeTracker.csproj            # Proyecto .NET 8 WPF
│
├── Themes/
│   └── DarkTheme.xaml             # Tema oscuro (colores, estilos, control templates)
│
├── Views/
│   ├── MainWindow.xaml/.cs        # Widget principal de PO
│   ├── JiraWindow.xaml/.cs        # Widget de integración Jira
│   ├── SettingsWindow.xaml/.cs    # Ventana de configuración
│   └── ReminderWindow.xaml/.cs    # Recordatorio diario de horas
│
├── Models/
│   ├── Models.cs                  # POProject, POTask, TimeEntry, LoginCredentials, WeekDay
│   └── JiraModels.cs             # JiraConfig, JiraProject, JiraIssue
│
├── Services/
│   ├── POApiService.cs            # Cliente HTTP para PO (login, proyectos, registro de horas)
│   ├── JiraApiService.cs          # Cliente HTTP para Jira REST API v3
│   ├── JiraConfigService.cs       # Almacenamiento seguro de config Jira (DPAPI)
│   ├── CredentialService.cs       # Credenciales PO y entradas locales (DPAPI)
│   └── LogService.cs              # Logger con rotación de archivos diaria
│
└── Assets/
    └── icon.ico                   # Ícono de la aplicación
```

---

## Cómo funciona la integración con PO

El widget se comunica con Project Open de la misma forma que un navegador:

1. **Login**: GET `sgplogin.aspx` → extrae `__VIEWSTATE` y campos GeneXus → POST con credenciales
2. **Proyectos/Tareas**: GET `registrodehoras.aspx` → parsea grids GeneXus (`FsgridproyectosContainerDataV`, `FsgridhorasContainerDataV_*`)
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
| Obtener issue | `GET /rest/api/3/issue/{key}` |
| Registrar worklog | `POST /rest/api/3/issue/{key}/worklog` |

El worklog se registra con `timeSpentSeconds` (precisión exacta) y el campo `started` en formato ISO 8601 con offset de timezone local.

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

## Seguridad

- Las credenciales de PO y el API token de Jira se cifran con **Windows DPAPI**
- Solo el usuario de Windows que los guardó puede descifrarlos
- No se transmiten datos a ningún servidor externo salvo PO y Jira

---

## Dependencias NuGet

| Paquete | Uso |
|---------|-----|
| `Hardcodet.NotifyIcon.Wpf` v1.1.0 | Ícono en la bandeja del sistema |
| `System.Security.Cryptography.ProtectedData` v8.0.0 | Cifrado DPAPI |
