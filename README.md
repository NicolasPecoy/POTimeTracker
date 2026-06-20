# PO Time Tracker — Widget de Registro de Horas

Widget de escritorio WPF para Windows que se integra con **Project Open (PO)** y **Jira Cloud** para registrar horas de trabajo directamente desde la bandeja del sistema. Incluye actualizaciones automáticas vía GitHub Releases.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue) ![WPF](https://img.shields.io/badge/UI-WPF-purple) ![Windows](https://img.shields.io/badge/OS-Windows%2010%2F11-0078D6)

---

## Índice

1. [Requisitos](#1-requisitos)
2. [Instalación](#2-instalación)
3. [Primera vez: configurar Project Open (PO)](#3-primera-vez-configurar-project-open-po)
4. [Configuración de ajustes](#4-configuración-de-ajustes)
5. [Configurar Jira (opcional)](#5-configurar-jira-opcional)
6. [Uso diario](#6-uso-diario)
7. [Actualizaciones automáticas](#7-actualizaciones-automáticas)
8. [Publicar una nueva versión](#8-publicar-una-nueva-versión)
9. [Build para desarrolladores](#9-build-para-desarrolladores)
10. [Referencia técnica](#10-referencia-técnica)

---

## 1. Requisitos

- **Windows 10 o Windows 11** (64-bit)
- **Sin instalación adicional** — el `.exe` descargado desde Releases incluye todo (.NET 8 embebido)
- Acceso de red al servidor de PO (`http://po.invenzis.com:8080` o el que corresponda)
- Acceso a internet para actualizaciones automáticas y Jira Cloud (opcional)

---

## 2. Instalación

### Opción A — Descarga directa (recomendado para usuarios finales)

1. Ir a **[Releases](https://github.com/NicolasPecoy/POTimeTracker/releases/latest)**
2. Descargar el archivo `POTimeTracker-X.Y.Z.exe`
3. Moverlo a la carpeta donde quieras que viva (ej: `C:\Users\TuUsuario\Apps\`)
4. Hacer doble clic para ejecutarlo

> No requiere instalador. No requiere .NET instalado por separado.

### Opción B — Clonar y compilar (para desarrolladores)

Ver la sección [9 — Build para desarrolladores](#9-build-para-desarrolladores).

---

## 3. Primera vez: configurar Project Open (PO)

Al ejecutar la app por primera vez aparece una ventana de **login**. Completar:

| Campo | Qué poner |
| --- | --- |
| **Usuario** | Tu usuario de PO (el mismo que usás en el navegador) |
| **Contraseña** | Tu contraseña de PO |

La app guarda las credenciales cifradas con **Windows DPAPI** en `%LOCALAPPDATA%\POTimeTracker\cred.dat`. Solo tu usuario de Windows puede leerlas.

### Verificar que el servidor esté configurado correctamente

Si tu empresa usa un servidor PO diferente al predeterminado (`http://po.invenzis.com:8080`):

1. Abrir **Configuración** (engranaje en el header del widget)
2. Buscar el campo **URL del servidor** y cambiarlo por la URL correcta
3. Guardar y reiniciar la sesión

---

## 4. Configuración de ajustes

Abrir la ventana de **Configuración** haciendo clic en el ícono de engranaje en el header del widget principal.

### Ajustes disponibles

| Ajuste | Qué hace | Valor por defecto |
| --- | --- | --- |
| **URL del servidor** | Dirección base del servidor PO | `http://po.invenzis.com:8080` |
| **Objetivo semanal de horas** | Horas que debés completar por semana; se usa para calcular el progreso en el strip semanal | `40` horas |
| **Hora del recordatorio** | A qué hora del día aparece el recordatorio de horas (rango: 14:00 a 23:00) | `17` (5 PM) |
| **Minutos del recordatorio** | Minutos de la hora del recordatorio (en intervalos de 5 min) | `15` |
| **Recordatorio el sábado** | Si activás esto, el recordatorio también aparece los sábados | Desactivado |
| **Recordatorio el domingo** | Si activás esto, el recordatorio también aparece los domingos | Desactivado |
| **Intervalo de re-login** | Cada cuántas horas la app renueva la sesión con PO automáticamente | `3.0` horas |
| **Fecha de inicio como hoy** | Si está activado, el widget siempre abre mostrando la fecha de hoy en lugar del último día usado | Activado |

### Dónde se guarda la configuración

Todos los ajustes se guardan en:

```
C:\Users\TuUsuario\AppData\Local\POTimeTracker\config.json
```

Podés editar ese archivo manualmente si la app no inicia o si necesitás restablecer valores.

---

## 5. Configurar Jira (opcional)

La integración con Jira permite registrar horas en issues de Jira Cloud al mismo tiempo que en PO.

### Paso 1 — Generar un API Token en Atlassian

1. Ir a **[Atlassian Account → Security → API tokens](https://id.atlassian.com/manage-profile/security/api-tokens)**
2. Click en **Create API token**
3. Darle un nombre (ej: `POTimeTracker`)
4. Click en **Create**
5. Copiar el token que aparece (solo se muestra una vez)

### Paso 2 — Conectar Jira desde la app

1. En el widget principal, hacer clic en el botón **J** del header (o clic derecho en el ícono de la bandeja → **Abrir Jira**)
2. En la ventana de Jira, abrir **Configuración de Jira**
3. Completar los campos según la tabla de abajo
4. Click en **Conectar a Jira**
5. Si los datos son correctos, aparece un mensaje de confirmación con tu nombre de usuario en Jira

| Campo | Ejemplo | Descripción |
| --- | --- | --- |
| **URL de Jira** | `https://tuempresa.atlassian.net` | La URL base de tu instancia de Jira Cloud. Siempre empieza con `https://` y termina en `.atlassian.net` |
| **Email** | `nombre@tuempresa.com` | El email con el que iniciás sesión en Atlassian |
| **API Token** | `ATATT3xFfGF...` | El token que copiaste en el Paso 1 |
| **Proyecto por defecto** *(opcional)* | `PROJ` | Clave corta del proyecto que más usás. Aparece preseleccionado al abrir el widget de Jira |

El token queda guardado cifrado en `%LOCALAPPDATA%\POTimeTracker\jira_token.dat`. La URL, email y proyecto por defecto quedan en `jira_config.json`.

### Alternativa: configurar con archivo .env

Si preferís no ingresar los datos desde la UI (por ejemplo, para distribución interna), podés crear un archivo `.env` en la misma carpeta donde está el `.exe`:

```env
JIRA_BASE_URL=https://tuempresa.atlassian.net
JIRA_EMAIL=nombre@tuempresa.com
JIRA_TOKEN=ATATT3xFfGF0...
```

> Si ya hay valores guardados desde la UI, esos tienen prioridad sobre el `.env`. El `.env` solo se usa como valor inicial cuando no hay configuración guardada.

---

## 6. Uso diario

### Registrar horas en PO

1. Hacer clic en el ícono de la bandeja del sistema para abrir el widget
2. Seleccionar la **fecha** en el strip semanal (por defecto, hoy)
3. Elegir el **Proyecto** en el primer desplegable
4. Elegir la **Tarea** en el segundo desplegable (se carga automáticamente según el proyecto)
5. Escribir las **horas** (o usar los botones rápidos: `0.5`, `1`, `2`, `4`, `8`)
6. Agregar una **nota** opcional (descripción del trabajo realizado)
7. Click en **Registrar Horas**

### Registrar horas en PO y Jira al mismo tiempo

1. En el formulario de PO, activar la casilla **"Registrar también en Jira"**
2. Aparecen campos extra: ingresar la clave del issue (ej: `PROJ-123`) o buscarla
3. Click en **Registrar Horas** → las horas se envían a PO y a Jira en un solo paso

### Registrar horas solo en Jira

1. Abrir el widget de Jira (botón **J** en el header o menú del tray)
2. En **Mis Issues** o **Buscar**, encontrar el issue
3. Seleccionar el issue → ingresar horas y nota
4. Click en **Registrar en Jira**

### Buscar issues en Jira

Dentro del widget de Jira, campo de búsqueda:

- **Por clave exacta**: escribir `PROJ-123` y presionar Enter
- **Por texto libre (JQL)**: escribir `assignee = currentUser() AND status != Done` y presionar Enter

### El recordatorio diario

A la hora configurada (default: 17:15), aparece una ventana pequeña que pregunta cuántas horas trabajaste ese día. Es un recordatorio para no olvidarse de registrar. Podés:

- Ingresar las horas y confirmar (se pre-completa el formulario de PO)
- Cerrarla si ya registraste las horas

---

## 7. Actualizaciones automáticas

Aproximadamente 5 segundos después de abrir la app, la app consulta silenciosamente a GitHub si hay una versión más nueva disponible.

### Si hay una actualización disponible

El texto del pie de ventana cambia a amarillo:

```
v1.0.0 - Widget de registro   ★ v1.1.0 disponible — click para actualizar
```

Al hacer **clic** en ese texto:

1. La app descarga el nuevo `.exe` a una carpeta temporal
2. Lanza un script que espera a que la app cierre
3. Reemplaza el `.exe` actual por el nuevo
4. Reinicia la app automáticamente

> Si el release de GitHub no tiene un `.exe` adjunto, se abre el navegador en la página del release para descargarlo manualmente.

### Si ya tenés la última versión

Al hacer clic en la versión del footer aparece:

```
Ya tenés la versión más reciente (v1.1.0)
```

---

## 8. Publicar una nueva versión

> Esta sección es para quien mantiene el repositorio.

### Cómo funciona el sistema

Cada vez que se sube un tag `vX.Y.Z` a GitHub, una **GitHub Action** construye automáticamente el `.exe` y crea un Release. Los usuarios ven el aviso de actualización la próxima vez que abren la app.

### Paso a paso para publicar

#### Paso 1 — Hacer los cambios al código

Modificar lo que sea necesario. Cuando esté listo para release, continuar.

#### Paso 2 — Actualizar la versión en el `.csproj`

Abrir `POTimeTracker.csproj` y cambiar la línea:

```xml
<!-- Cambiar esto -->
<Version>1.0.0</Version>

<!-- Por la versión nueva (ejemplo: nueva feature) -->
<Version>1.1.0</Version>
```

**Regla para elegir qué número incrementar:**

| Tipo de cambio | Qué incrementar | Ejemplo |
| --- | --- | --- |
| Corrección de bug pequeño | Tercer número (Patch) | `1.0.0` → `1.0.1` |
| Feature nueva o mejora visible | Segundo número (Minor) | `1.0.0` → `1.1.0` |
| Rediseño o cambio grande | Primer número (Major) | `1.0.0` → `2.0.0` |

#### Paso 3 — Commitear

```powershell
git add POTimeTracker.csproj
git commit -m "Bump version to 1.1.0"
```

#### Paso 4 — Crear el tag

El número del tag **debe coincidir exactamente** con el del `.csproj` (con `v` adelante):

```powershell
git tag v1.1.0
```

#### Paso 5 — Subir a GitHub

```powershell
git push origin master
git push origin v1.1.0
```

#### Paso 6 — Esperar que GitHub Actions compile

El proceso es automático. Para seguirlo:

- Ir a `https://github.com/NicolasPecoy/POTimeTracker/actions`
- Verás un workflow corriendo llamado `Release`

Lo que hace automáticamente:

1. Verifica que el tag (`v1.1.0`) coincida con el `.csproj` (`1.1.0`) — **si no coinciden, falla con error claro**
2. Compila el proyecto en modo Release
3. Genera un `.exe` self-contained (incluye .NET 8, no requiere instalación)
4. Crea un Release en GitHub con el `.exe` adjunto

En unos minutos, el release aparece en:
`https://github.com/NicolasPecoy/POTimeTracker/releases`

Los usuarios verán el aviso la próxima vez que abran la app.

#### Qué pasa si el tag y el .csproj no coinciden

El build falla inmediatamente con este mensaje:

```
MISMATCH: .csproj dice '1.0.0' pero el tag dice '1.1.0'.
Actualizá <Version> en el .csproj antes de taggear.
```

Esto evita publicar un `.exe` con número de versión incorrecto.

#### Cheat sheet (resumen rápido)

```powershell
# 1. Editar POTimeTracker.csproj → cambiar <Version>X.Y.Z</Version>

# 2. Commitear
git add POTimeTracker.csproj
git commit -m "Bump version to X.Y.Z"

# 3. Crear tag y subir todo
git push origin master
git tag vX.Y.Z
git push origin vX.Y.Z

# GitHub Actions hace el resto automáticamente
```

---

## 9. Build para desarrolladores

### Requisitos para compilar

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (opcional) con carga de trabajo **.NET Desktop Development**

### Clonar y ejecutar

```powershell
git clone https://github.com/NicolasPecoy/POTimeTracker.git
cd POTimeTracker
dotnet restore
dotnet run
```

### Generar el .exe manualmente

```powershell
dotnet publish POTimeTracker.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o publish/
```

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

El ejecutable queda en `publish\POTimeTracker.exe`. Pesa ~60-80 MB porque incluye el runtime de .NET.

### En Visual Studio 2022

1. Abrir `POTimeTracker.sln` (doble clic)
2. Click derecho en el proyecto → **Restore NuGet Packages** (si no se hace automático)
3. Presionar **F5** para compilar y ejecutar en modo Debug

---

## 10. Referencia técnica

### Archivos de datos locales

Todos los archivos de la app viven en:

```text
C:\Users\TuUsuario\AppData\Local\POTimeTracker\
```

| Archivo | Contenido | Formato |
| --- | --- | --- |
| `cred.dat` | Credenciales PO (usuario + contraseña) | Binario cifrado con DPAPI |
| `config.json` | Ajustes del widget (servidor, recordatorio, objetivo, etc.) | JSON |
| `entries.json` | Historial de horas registradas (últimos 3 meses) | JSON |
| `jira_config.json` | Config de Jira (URL, email, proyecto por defecto, habilitado) | JSON |
| `jira_token.dat` | API token de Jira | Binario cifrado con DPAPI |
| `logs\app-YYYY-MM-DD.log` | Logs de errores y advertencias | Texto (rotación de 7 días) |

Para **resetear** la app completamente, eliminar toda la carpeta `POTimeTracker` dentro de `AppData\Local`.

### Cómo funciona la integración con PO

La app se comunica con PO imitando exactamente lo que hace un navegador web:

1. **Login**: GET `sgplogin.aspx` → extrae tokens `__VIEWSTATE` GeneXus → POST con usuario y contraseña
2. **Cargar proyectos/tareas**: GET `registrodehoras.aspx` → parsea los grids del formulario GeneXus
3. **Registrar horas**: GET fresh page state → localiza la celda exacta del grid → POST con evento `CONFIRMAR`
4. **Renovar sesión**: automáticamente cada N horas (configurable, default 3h)

### Cómo funciona la integración con Jira

Usa la **Jira Cloud REST API v3** con autenticación Basic (email + API token en Base64):

| Operación | Endpoint |
| --- | --- |
| Verificar conexión | `GET /rest/api/3/myself` |
| Listar proyectos | `GET /rest/api/3/project/search?maxResults=50&orderBy=name` |
| Buscar issues | `GET /rest/api/3/search?jql=...&maxResults=50` |
| Obtener issue por clave | `GET /rest/api/3/issue/{key}` |
| Registrar worklog | `POST /rest/api/3/issue/{key}/worklog` |
| Ver worklogs de una fecha | `GET /rest/api/3/issue/{key}/worklog` |

### Cómo funciona el auto-update

1. Al iniciar (5 segundos después para no bloquear la UI), consulta:
   `https://api.github.com/repos/NicolasPecoy/POTimeTracker/releases/latest`
2. Compara el número de versión del último release con la versión del `.exe` actual
3. Si hay una versión más nueva:
   - Muestra el aviso en el footer (texto amarillo)
   - Al hacer clic, descarga el `.exe` a `%TEMP%\POTimeTracker_update.exe`
   - Crea un script batch en `%TEMP%\pott_update.bat` que:
     - Espera a que el proceso `POTimeTracker.exe` termine
     - Copia el nuevo `.exe` sobre el viejo
     - Lanza la nueva versión
     - Se auto-elimina
   - La app cierra y el script hace el reemplazo en segundo plano

### Seguridad de las credenciales

- **Windows DPAPI**: las credenciales y el API token de Jira se cifran con la cuenta de Windows del usuario. Solo ese usuario (en ese equipo) puede descifrarlos. No hay contraseña maestra.
- **Sin servidores propios**: los datos solo van a tu instancia de PO y a tu instancia de Jira Cloud.
- **HTTPS**: todas las comunicaciones con Jira y GitHub usan HTTPS.

### Estructura del proyecto

```text
POTimeTracker/
├── .github/
│   └── workflows/
│       └── release.yml           # Build y release automático al taggear
│
├── POTimeTracker.csproj          # Proyecto .NET 8 WPF — define <Version>
├── App.xaml / App.xaml.cs        # Entry point, manejo de errores globales
│
├── Themes/
│   └── DarkTheme.xaml           # Tema oscuro (colores, estilos, templates)
│
├── Views/
│   ├── MainWindow.xaml/.cs      # Widget principal de PO
│   ├── JiraWindow.xaml/.cs      # Widget de Jira
│   ├── SettingsWindow.xaml/.cs  # Ventana de configuración
│   └── ReminderWindow.xaml/.cs  # Recordatorio diario
│
├── Models/
│   ├── Models.cs                # POProject, POTask, TimeEntry, LoginCredentials
│   └── JiraModels.cs            # JiraConfig, JiraProject, JiraIssue
│
├── Services/
│   ├── POApiService.cs          # Cliente HTTP para PO (parsing GeneXus)
│   ├── JiraApiService.cs        # Cliente Jira REST API v3
│   ├── JiraConfigService.cs     # Persistencia config Jira (DPAPI)
│   ├── CredentialService.cs     # Credenciales PO + config local (DPAPI)
│   ├── UpdateService.cs         # Detección y descarga de actualizaciones
│   ├── LogService.cs            # Logger con rotación diaria de archivos
│   └── EnvLoader.cs             # Cargador de archivo .env
│
└── Assets/
    └── icon.ico                 # Ícono de la aplicación
```

### Dependencias NuGet

| Paquete | Versión | Para qué se usa |
| --- | --- | --- |
| `Hardcodet.NotifyIcon.Wpf` | 1.1.0 | Ícono en la bandeja del sistema (system tray) |
| `System.Security.Cryptography.ProtectedData` | 8.0.0 | Cifrado DPAPI para credenciales |
