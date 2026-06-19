# Guía de Release y Auto-Update — POTimeTracker

## Cómo funciona el sistema

El proyecto tiene **dos partes** que trabajan juntas:

1. **GitHub Actions** (`release.yml`) — compila el `.exe` y lo publica en GitHub Releases cada vez que pusheás un tag `vX.Y.Z`.
2. **UpdateService** (dentro de la app) — al arrancar, consulta la API de GitHub, compara versiones, y si hay una nueva le ofrece al usuario descargarla e instalarse solo reemplazando el `.exe` en disco.

---

## Paso 1 — Requisitos previos (solo la primera vez)

### En tu máquina de desarrollo

- .NET 8 SDK instalado ([dotnet.microsoft.com](https://dotnet.microsoft.com/download))
- Git instalado y configurado
- El repo pusheado a GitHub (ya está en `NicolasPecoy/POTimeTracker`)
- El repo tiene que tener **GitHub Actions habilitado** (en Settings → Actions → Allow all actions)

### En cualquier máquina de usuario final

- Solo necesitan el `.exe` descargado de GitHub Releases una vez.
- No necesitan instalar .NET ni nada extra (el exe es self-contained).
- Las actualizaciones futuras las maneja la misma app.

---

## Paso 2 — Publicar una nueva versión (flujo completo)

### 2.1 — Actualizá la versión en el `.csproj`

Abrí [POTimeTracker.csproj](POTimeTracker.csproj) y cambiá la línea `<Version>`:

```xml
<Version>1.2.0</Version>
```

> **Importante:** el número acá tiene que coincidir exactamente con el tag que vas a crear. Si no coinciden, el workflow de GitHub falla a propósito con un error de mismatch.

### 2.2 — Commiteá el cambio

```powershell
git add POTimeTracker.csproj
git commit -m "Bump version to 1.2.0"
git push
```

### 2.3 — Creá y pusheá el tag

```powershell
git tag v1.2.0
git push origin v1.2.0
```

Eso es todo lo que tenés que hacer. GitHub Actions se dispara automáticamente.

### 2.4 — Qué hace GitHub Actions (automático)

El workflow `.github/workflows/release.yml` hace esto solo:

1. Descarga el código fuente
2. Instala .NET 8
3. Verifica que la versión del tag coincide con la del `.csproj`
4. Compila con `dotnet publish` en modo Release, self-contained, single file para `win-x64`
5. Renombra el exe a `POTimeTracker-1.2.0.exe`
6. Crea el GitHub Release con el exe adjunto

Podés ver el progreso en la pestaña **Actions** del repo en GitHub.

---

## Paso 3 — Cómo se actualiza la app en las máquinas de los usuarios

### Lo que ve el usuario

1. Al abrir la app, `UpdateService` consulta `https://api.github.com/repos/NicolasPecoy/POTimeTracker/releases/latest` (5 segundos después de arrancar, para no bloquear el inicio).
2. Si la versión del release es mayor que la instalada, el texto de versión en la app cambia a color **amarillo** y muestra algo como:

   ```
   v1.1.0 - Widget de registro  ★ v1.2.0 disponible
   ```

3. El usuario hace click en ese texto, aparece un diálogo preguntando si quiere actualizar.
4. Si acepta, la app:
   - Descarga el nuevo `.exe` a la carpeta temporal del sistema
   - Lanza un script `.bat` que espera a que la app cierre
   - Cierra la app
   - El script reemplaza el `.exe` viejo por el nuevo
   - Reinicia la app automáticamente
   - Se borra a sí mismo

### El usuario no necesita hacer nada manualmente — solo hacer click en "Sí".

---

## Paso 4 — Primera instalación en una máquina nueva

1. Ir a `https://github.com/NicolasPecoy/POTimeTracker/releases/latest`
2. Descargar `POTimeTracker-X.Y.Z.exe`
3. Copiarlo a cualquier carpeta (Escritorio, Documentos, etc.)
4. Ejecutarlo — no requiere instalación

> La app se registra sola en el inicio automático de Windows (en `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) la primera vez que se ejecuta.

---

## Resumen del flujo completo

```
Vos (dev)                          GitHub Actions                    Usuario final
─────────────                      ──────────────                    ─────────────
1. Cambiás <Version> en .csproj
2. git commit + git push
3. git tag v1.2.0
4. git push origin v1.2.0   ──►   Compila el .exe
                                   Crea el GitHub Release      ──►  App detecta nueva versión
                                                                     Usuario hace click → se actualiza sola
```

---

## Troubleshooting

| Problema | Causa | Solución |
|---|---|---|
| El workflow falla con "MISMATCH" | La versión en `.csproj` no coincide con el tag | Actualizá `<Version>` en el `.csproj` antes de taggear |
| La app no detecta la actualización | La versión del `.exe` instalado es la misma o mayor | Verificar que el tag en GitHub es mayor que la versión actual |
| Error al descargar la actualización | Sin conexión o el asset del release no es un `.exe` | El workflow genera el exe con nombre `POTimeTracker-X.Y.Z.exe` — verificar que el release tiene ese asset |
| El `.bat` de actualización falla | El exe está en una carpeta protegida (ej: `Program Files`) | Mover el `.exe` a una carpeta sin restricciones de escritura (Escritorio, Documentos) |
| Actions no se dispara | GitHub Actions deshabilitado o el tag no tiene formato `v*.*.*` | Verificar Settings → Actions en el repo, y que el tag empieza con `v` |

---

## Versioning recomendado (semver)

- `v1.0.1` — bug fix o cambio menor
- `v1.1.0` — feature nueva sin romper compatibilidad
- `v2.0.0` — cambio grande o breaking change
