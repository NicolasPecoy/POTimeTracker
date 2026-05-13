# 🕐 PO Time Tracker — Widget de Registro de Horas

Widget de escritorio WPF moderno que se integra con **Project Open** para registrar horas de trabajo directamente desde la bandeja del sistema (system tray) de Windows.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue) ![WPF](https://img.shields.io/badge/UI-WPF-purple) ![Windows](https://img.shields.io/badge/OS-Windows-0078D6)

---

## ✨ Características

- **System Tray Widget** — Vive en la bandeja del sistema, se abre con un clic
- **Login persistente** — Credenciales encriptadas con Windows DPAPI
- **Integración con PO** — Se conecta a `registrodehoras.aspx` automáticamente
- **Navegación por fecha** — Strip semanal interactivo con resumen de horas
- **Registro rápido** — Botones de horas rápidas (0.5, 1, 2, 4, 8h)
- **UI moderna oscura** — Diseño tipo Fluent/WinUI con animaciones
- **Fallback local** — Si el servidor no responde, guarda localmente
- **Log de errores** — Registro automático de errores en archivos de log con rotación de 7 días

---

## 🚀 Requisitos

- **Windows 10/11**
- **.NET 8 SDK** — [Descargar](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Visual Studio 2022** (recomendado) o VS Code con C# extension

---

## 📦 Instalación y Build

### Opción 1: Visual Studio
```
1. Abrir POTimeTracker.csproj en Visual Studio 2022
2. Click derecho en el proyecto → "Restore NuGet Packages"
3. Presionar F5 para compilar y ejecutar
```

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
El .exe se genera en `bin/Release/net8.0-windows/win-x64/publish/`

---

## 🔧 Configuración

### Primera ejecución
1. Ejecutar `POTimeTracker.exe`
2. Aparece la ventana de login
3. Configurar:
   - **Servidor**: `http://po.invenzis.com:8080` (ya viene preconfigurado)
   - **Usuario**: Tu usuario de PO
   - **Contraseña**: Tu contraseña de PO
4. Marcar "Recordar sesión" para auto-login
5. Click en "Iniciar Sesión"

### Uso diario
- **Click en el ícono** de la bandeja del sistema para abrir/cerrar
- Seleccionar proyecto → tarea → horas → click "Registrar"
- Navegar entre días con las flechas o el strip semanal
- **Click derecho** en el ícono para acceder al menú contextual

### Inicio automático con Windows
Para que inicie con Windows, crear un acceso directo del .exe en:
```
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
```

---

## 🏗️ Estructura del Proyecto

```
POTimeTracker/
├── App.xaml                    # Entry point
├── POTimeTracker.csproj        # Proyecto .NET 8 WPF
├── Themes/
│   └── DarkTheme.xaml          # Tema oscuro completo (colores, estilos, templates)
├── Views/
│   ├── MainWindow.xaml         # UI principal del widget
│   └── MainWindow.xaml.cs      # Lógica del widget
├── Models/
│   └── Models.cs               # POProject, POTask, TimeEntry, etc.
├── Services/
│   ├── CredentialService.cs    # Almacenamiento seguro de credenciales (DPAPI)
│   ├── LogService.cs           # Logger de errores con rotación de archivos
│   └── POApiService.cs         # Cliente HTTP para login y API de PO
├── Converters/
│   └── Converters.cs           # Converters de XAML (colores, visibilidad, etc.)
└── Assets/
    └── icon.ico                # Ícono de la aplicación (agregar el tuyo)
```

---

## 📝 Logs

Los logs se guardan en `%LOCALAPPDATA%\POTimeTracker\logs\` con un archivo por día:

```
app-2025-05-13.log
app-2025-05-12.log
...
```

Se capturan automáticamente:
- **Errores de conexión** al servidor de PO (login, carga de proyectos, envío de horas)
- **Errores de archivos** al leer/escribir credenciales, configuración y registros locales
- **Excepciones no manejadas** de cualquier parte de la aplicación (UI thread, background, Tasks)
- **Advertencias** de re-login automático y registro de inicio con Windows

Los archivos tienen más de 7 días se eliminan automáticamente.

---

## 🔐 Seguridad

- Las credenciales se encriptan con **Windows DPAPI** (Data Protection API)
- Solo el usuario de Windows que las guardó puede desencriptarlas
- Se almacenan en `%LOCALAPPDATA%/POTimeTracker/cred.dat`
- La sesión HTTP mantiene las cookies de ASP.NET automáticamente

---

## 🔌 Cómo funciona la integración con PO

El widget se comunica con Project Open de la misma forma que un navegador:

1. **Login**: GET a `sgplogin.aspx` → extrae `__VIEWSTATE` → POST con credenciales
2. **Proyectos**: GET a `registrodehoras.aspx` → parsea los `<select>` del HTML
3. **Registro**: POST a `registrodehoras.aspx` con proyecto, tarea, horas y notas

Los nombres de campos (como `txtUsuario`, `ddlProyecto`, etc.) se detectan automáticamente
probando las variantes más comunes de ASP.NET WebForms.

---

## ⚠️ Notas

- Si los nombres de los campos del formulario de PO son diferentes a los estándar,
  se pueden ajustar en `POApiService.cs` en los arrays de `DetectFieldName`
- El ícono de system tray requiere un archivo `.ico`. Si no lo tenés,
  el ícono por defecto de .NET se usará automáticamente
- Para agregar un ícono personalizado, colocar `icon.ico` en la carpeta `Assets/`

---

## 📋 Dependencias NuGet

| Paquete | Uso |
|---------|-----|
| `Hardcodet.NotifyIcon.Wpf` | Ícono en la bandeja del sistema |
| `Microsoft.Toolkit.Mvvm` | Helpers MVVM |
| `System.Security.Cryptography.ProtectedData` | Encriptación DPAPI |
