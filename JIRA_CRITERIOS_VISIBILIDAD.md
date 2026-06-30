# Criterios de visibilidad de Issues y Proyectos en Jira

## 1. Dropdown de proyectos (filtro de proyecto)

El dropdown carga proyectos mediante:
```
GET /rest/api/3/project/search?maxResults=50&orderBy=name
```

**Reglas:**
- Solo aparecen los **primeros 50 proyectos** ordenados alfabéticamente por nombre
- El usuario autenticado (cuenta del `.env`) debe tener **acceso de lectura** al proyecto en Jira
- No hay filtro por usuario asignado — aparecen todos los proyectos accesibles

**Por qué puede faltar un proyecto:**
| Causa | Explicación |
|-------|-------------|
| Más de 50 proyectos | Se toman solo los primeros 50 por orden alfabético |
| Sin acceso al proyecto | La cuenta del `.env` no tiene permisos en ese proyecto |

> **✓ Resuelto:** `GetProjectsAsync` pagina automáticamente de a 50 hasta traer todos los proyectos accesibles.

---

## 2. Issues en la ventana principal (panel "Registrar también en Jira")

### JQL que se ejecuta por defecto:
```
assignee = currentUser() AND statusCategory != Done ORDER BY updated DESC
```

### Con filtro de proyecto seleccionado:
```
project = "CLAVE" AND assignee = currentUser() AND statusCategory != Done ORDER BY updated DESC
```

### Con "Mostrar completados" activado:
```
assignee = currentUser() ORDER BY updated DESC
```

**Para que un issue aparezca en la lista, debe cumplir TODO lo siguiente:**

| Criterio | Valor requerido |
|----------|----------------|
| **Asignado a** | El usuario de la cuenta configurada en `.env` (`JIRA_EMAIL`) |
| **Estado** | Cualquier estado cuya categoría **no sea "Done"** (a menos que se active "Mostrar completados") |
| **Proyecto** | Cualquiera (si dropdown = "Todos") o el proyecto seleccionado en el filtro |
| **Posición** | Dentro de los primeros 100 resultados de la página inicial |

---

## 3. Ventana de Jira (JiraWindow)

Mismos criterios que el panel principal. Los issues se cargan con el mismo JQL.

---

## Caso West Pharma (WP)

El proyecto **WEST PHARMA** existe en Jira, pero sus issues no aparecen porque:

1. **Los issues no están asignados a `jira.admin@invenzis.com`** — la cuenta del `.env` es un usuario administrador genérico, no el usuario real que tiene las tareas asignadas. El JQL filtra `assignee = currentUser()`, donde `currentUser` es `jira.admin@invenzis.com`.

2. Las tareas de West Pharma (WP-54, WP-53, etc.) están asignadas a usuarios específicos (Bettina Javier u otros), no a la cuenta admin.

### Diagnóstico rápido

Para verificar qué devuelve la API para un usuario, se puede buscar directamente por clave:
- En el buscador de la app, escribir `WP-54` y presionar Enter → si aparece, el issue existe y es accesible
- Si no aparece, la cuenta no tiene acceso de lectura a ese issue

---

## Soluciones posibles

### Opción A: Que cada usuario use su propio token (recomendada)
En lugar de credenciales compartidas en `.env`, la ventana de configuración de Jira (`JiraWindow`) permite que **cada usuario ingrese su propio email y API token**. Así `currentUser()` devuelve sus propias tareas asignadas.

### Opción B: Cambiar el JQL para no filtrar por assignee
En `JiraApiService.BuildMyIssuesJql` cambiar:
```csharp
// Actual — solo issues del usuario actual
"assignee = currentUser() AND statusCategory != Done ORDER BY updated DESC"

// Alternativa — todos los issues no completados del proyecto
"statusCategory != Done ORDER BY updated DESC"
```
**Desventaja:** Devuelve todos los issues del proyecto (puede ser muy largo).

### Opción C: Aumentar el límite de proyectos
En `JiraApiService.GetProjectsAsync`:
```csharp
// Cambiar de 50 a 100 (máximo de Jira Cloud)
$"{_baseUrl}/rest/api/3/project/search?maxResults=100&orderBy=name"
```

---

## Resumen visual

```
¿Por qué no aparece el issue?
         │
         ├── ¿El issue está asignado a jira.admin@invenzis.com?
         │         NO → No aparecerá con el JQL actual
         │         SÍ → continúa
         │
         ├── ¿El estado del issue es "Done" o equivalente?
         │         SÍ → Solo aparece si se activa "Mostrar completados"
         │         NO → continúa
         │
         └── ¿Está dentro de los primeros 100 resultados ordenados por fecha?
                   NO → No aparecerá en la carga inicial
                   SÍ → Debería aparecer ✓
```
