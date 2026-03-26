# Plantillas Oficiales de Pipelines — Azure DevOps

**Versión:** 1.0  
**Fecha:** Marzo 2026  
**Enfoque:** CI/CD corporativo para aplicaciones web desplegadas en IIS On-Prem

---

## Tabla de Contenido

1. [Estructura de Archivos](#1-estructura-de-archivos)
2. [Arquitectura de 3 Capas](#2-arquitectura-de-3-capas)
3. [Flujo de Ejecución](#3-flujo-de-ejecución)
4. [Descripción de Templates](#4-descripción-de-templates)
5. [Dependencias entre Templates](#5-dependencias-entre-templates)
6. [Requisitos por Etapa](#6-requisitos-por-etapa)
7. [Cómo Adaptar a un Nuevo Proyecto](#7-cómo-adaptar-a-un-nuevo-proyecto)
8. [Variables Requeridas por Entorno](#8-variables-requeridas-por-entorno)
9. [Estrategia de Branching](#9-estrategia-de-branching)
10. [Seguridad](#10-seguridad)
11. [Consideraciones Especiales](#11-consideraciones-especiales)
12. [Troubleshooting](#12-troubleshooting)
13. [Recomendaciones y Mejoras Futuras](#13-recomendaciones-y-mejoras-futuras)

---

## 1. Estructura de Archivos

```
Templates oficiales/
├── README.md                         ← Este archivo
├── templates/
│   ├── steps-ci.yml                  ← Steps: build, test, scan, publish
│   ├── steps-cd-iis.yml              ← Steps: backup, deploy IIS, health check
│   ├── stage-ci.yml                  ← Stage wrapper CI (Microsoft-hosted)
│   └── stage-cd.yml                  ← Stage wrapper CD (self-hosted)
└── pipelines/
    ├── dotnet-webapp.yml             ← Ejemplo completo: app .NET
    └── nodejs-webapp.yml             ← Ejemplo completo: app Node.js
```

---

## 2. Arquitectura de 3 Capas

Las plantillas siguen una arquitectura jerárquica de 3 capas. Cada capa tiene una responsabilidad clara:

```
CAPA 3 — Pipeline (pipelines/*.yml)
│   Define: triggers, stages invocados, valores específicos del proyecto
│   Ejemplo: "mi app usa .NET 8 y se despliega en estos servidores"
│
├── CAPA 2 — Stage (templates/stage-*.yml)
│   │   Define: pool de agentes, environment, variable group, dependencias
│   │   Ejemplo: "CI corre en MS-hosted, CD corre en Pool-Test"
│   │
│   └── CAPA 1 — Steps (templates/steps-*.yml)
│           Define: los comandos reales (build, test, deploy, backup)
│           Ejemplo: "ejecutar dotnet build, msdeploy.exe, health check"
```

### ¿Por qué 3 capas?

| Capa | ¿Cuándo se modifica? | ¿Quién la modifica? |
|------|---------------------|---------------------|
| **Pipeline** | Al crear un nuevo proyecto o cambiar configuración de triggers | Equipo de desarrollo |
| **Stage** | Raramente — al cambiar pools, agregar entornos | DevOps / Infraestructura |
| **Steps** | Solo al mejorar los procesos de build/deploy | DevOps / Arquitectura |

---

## 3. Flujo de Ejecución

```
┌─────────────────────────────────────────────────────────────────────┐
│                         Pipeline Principal                           │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │  STAGE: CI                                                     │  │
│  │  Pool: Microsoft-hosted (efímero)                              │  │
│  │  ┌──────────────────────────────────────────────────────────┐  │  │
│  │  │  1. Setup (instalar SDK/runtime)                         │  │  │
│  │  │  2. Build (compilar/transpilar)                          │  │  │
│  │  │  3. Test (ejecutar tests unitarios)                      │  │  │
│  │  │  4. Security Scan (SAST + Dependency Scan)               │  │  │
│  │  │  5. Publish (generar artefacto inmutable versionado)      │  │  │
│  │  └──────────────────────────────────────────────────────────┘  │  │
│  └──────────────────────────┬─────────────────────────────────────┘  │
│                              │ artefacto                              │
│  ┌──────────────────────────▼─────────────────────────────────────┐  │
│  │  STAGE: CD_Test                                                │  │
│  │  Pool: Self-hosted (Pool-Test)                                 │  │
│  │  ┌──────────────────────────────────────────────────────────┐  │  │
│  │  │  1. Download artefacto                                   │  │  │
│  │  │  2. Backup pre-deploy (+ rotación)                       │  │  │
│  │  │  3. Verificar sitio IIS                                  │  │  │
│  │  │  4. Deploy via Web Deploy (msdeploy.exe)                 │  │  │
│  │  │  5. Health Check (+ auto-rollback si falla)              │  │  │
│  │  │  6. Deploy Summary                                       │  │  │
│  │  └──────────────────────────────────────────────────────────┘  │  │
│  └──────────────────────────┬─────────────────────────────────────┘  │
│                              │ mismo artefacto                        │
│  ┌──────────────────────────▼─────────────────────────────────────┐  │
│  │  STAGE: CD_Prod  ⚠️ REQUIERE APROBACIÓN                       │  │
│  │  Pool: Self-hosted (Pool-Prod)                                 │  │
│  │  Condición: Solo desde rama main                               │  │
│  │  (mismos steps que CD_Test)                                    │  │
│  └────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 4. Descripción de Templates

### `templates/steps-ci.yml` — Steps de CI

**Propósito:** Compilar, ejecutar tests, escanear seguridad y publicar un artefacto inmutable.

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `technology` | string | `dotnet` | `dotnet` \| `nodejs` \| `custom` |
| `artifactName` | string | `WebApp` | Nombre base del artefacto |
| `buildConfiguration` | string | `Release` | Configuración de build (.NET) |
| `dotnetVersion` | string | `8.x` | Versión del SDK .NET |
| `projectPath` | string | — | Ruta al `.csproj` o `.sln` |
| `runTests` | boolean | `true` | Ejecutar tests unitarios |
| `testProjectPath` | string | — | Ruta explícita al proyecto de tests |
| `nodeVersion` | string | `20.x` | Versión de Node.js |
| `packageManager` | string | `npm` | `npm` \| `yarn` |
| `buildScript` | string | `build` | Script de package.json para build |
| `testScript` | string | `test` | Script para tests |
| `outputFolder` | string | `dist` | Carpeta de salida del build (Node.js) |
| `enableSecurityScan` | boolean | `true` | Habilitar SAST (MSDO) |
| `enableDependencyScan` | boolean | `true` | Habilitar escaneo de dependencias |
| `prePublishSteps` | stepList | `[]` | Steps custom antes de publicar |

---

### `templates/steps-cd-iis.yml` — Steps de CD (IIS + Web Deploy)

**Propósito:** Backup, deploy via Web Deploy, health check y auto-rollback.

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `artifactName` | string | `WebApp` | Debe coincidir con el de CI |
| `deployPath` | string | `C:\inetpub\wwwroot\WebApp` | Ruta física del sitio IIS |
| `backupPath` | string | `C:\deploy-backups\WebApp` | Carpeta de backups rotativos |
| `msdeployPath` | string | `C:\Program Files\IIS\...` | Ruta a msdeploy.exe |
| `healthCheckEndpoint` | string | `/health` | Endpoint de verificación de salud |
| `healthCheckRetries` | number | `3` | Reintentos de health check |
| `healthCheckDelaySeconds` | number | `10` | Segundos entre reintentos |
| `enableAutoRollback` | boolean | `true` | Rollback automático si falla el health check |
| `maxBackups` | number | `5` | Máximo de backups a retener |
| `environment` | string | — | Nombre del entorno (informativo) |

**Variables requeridas en el Variable Group:**
- `WebsiteName` — Nombre del sitio en IIS
- `SiteUrl` — URL completa del sitio
- `WebDeployUser` — Cuenta de servicio
- `WebDeployPassword` 🔒 — Secreto

---

### `templates/stage-ci.yml` — Stage Wrapper CI

**Propósito:** Encapsular steps-ci.yml con pool Microsoft-hosted.

- Pool: `vmImage` configurable (default: `windows-latest`)
- Job: `Build` (job estándar, no deployment)
- Pasa todos los parámetros a `steps-ci.yml`

---

### `templates/stage-cd.yml` — Stage Wrapper CD

**Propósito:** Encapsular steps-cd-iis.yml con environment, aprobaciones, variable group y pool self-hosted.

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `environment` | string | — | Nombre del Environment en Azure DevOps |
| `pool` | string | — | Nombre del Agent Pool self-hosted |
| `variableGroup` | string | — | Variable Group con secretos del entorno |
| `dependsOn` | string | `CI` | Stage del que depende |
| `condition` | string | `succeeded()` | Condición de ejecución |
| + todos los parámetros de `steps-cd-iis.yml` ||||

---

## 5. Dependencias entre Templates

```
Pipeline (.yml del proyecto)
│
├──► stage-ci.yml
│    └──► steps-ci.yml
│
├──► stage-cd.yml (Test)
│    └──► steps-cd-iis.yml
│
└──► stage-cd.yml (Prod)
     └──► steps-cd-iis.yml
```

**Reglas de dependencia:**
- `stage-ci.yml` **siempre** invoca `steps-ci.yml` (mismo directorio)
- `stage-cd.yml` **siempre** invoca `steps-cd-iis.yml` (mismo directorio)
- El pipeline invoca los stages con rutas relativas (`../templates/stage-*.yml`)
- `CD_Test` depende de `CI`; `CD_Prod` depende de `CD_Test`

---

## 6. Requisitos por Etapa

### Para que CI sea exitoso ✅

| Requisito | Dónde configurar |
|-----------|-----------------|
| Extensión Microsoft Security DevOps instalada | Azure DevOps Marketplace |
| SDK de la tecnología disponible en el agente | Automático (MS-hosted) |
| Proyecto compila sin errores | Código fuente |
| Tests pasan (si `runTests: true`) | Código fuente |

### Para que CD sea exitoso ✅

| Requisito | Dónde configurar |
|-----------|-----------------|
| Agent Pool self-hosted creado y con agente Online | Azure DevOps > Organization Settings > Agent Pools |
| Web Deploy 3.6+ instalado en el servidor | `guia-web-deploy-iis.md` — Sección 2 |
| WMSVC habilitado y corriendo | `guia-web-deploy-iis.md` — Sección 4 |
| Sitio IIS creado (via bootstrap) | `guia-web-deploy-iis.md` — Sección 8 |
| Delegación de Web Deploy configurada | `guia-web-deploy-iis.md` — Sección 5 |
| Variable Group creado con todas las variables | Azure DevOps > Pipelines > Library |
| Environment creado | Azure DevOps > Pipelines > Environments |
| Aprobaciones configuradas (Prod) | Environment > Approvals and checks |
| Cuenta de servicio con permisos NTFS | `guia-web-deploy-iis.md` — Sección 3 |

---

## 7. Cómo Adaptar a un Nuevo Proyecto

### Paso 1: Copiar el pipeline ejemplo

```bash
# Para .NET:
cp "DevOps/Templates oficiales/pipelines/dotnet-webapp.yml" "pipelines/mi-nuevo-proyecto.yml"

# Para Node.js:
cp "DevOps/Templates oficiales/pipelines/nodejs-webapp.yml" "pipelines/mi-nuevo-proyecto.yml"
```

### Paso 2: Reemplazar los placeholders

En el archivo copiado, buscar todos los comentarios `<!-- CAMBIAR -->` y reemplazar con los valores de tu proyecto:

```yaml
# Ejemplo para un proyecto "PortalRH"
- template: ../templates/stage-ci.yml
  parameters:
    technology: 'dotnet'
    dotnetVersion: '8.x'
    projectPath: 'src/PortalRH/PortalRH.csproj'
    artifactName: 'PortalRH'
```

### Paso 3: Crear recursos en Azure DevOps

1. **Variable Groups** en Pipelines > Library:
   - `vg-portalrh-test` con variables del entorno Test
   - `vg-portalrh-prod` con variables del entorno Prod

2. **Environments** en Pipelines > Environments:
   - `Test` (probablemente ya existe — es compartido)
   - `Prod` (probablemente ya existe — es compartido)

3. **Agent Pools**: ya deben existir (`Pool-Test`, `Pool-Prod`)

### Paso 4: Crear el pipeline en Azure DevOps

1. Ir a Pipelines > New Pipeline
2. Seleccionar Azure Repos Git > tu repositorio
3. Existing Azure Pipelines YAML file > `pipelines/mi-nuevo-proyecto.yml`
4. Save (o Run para probar)

### Paso 5: Preparar el servidor (si es primera vez)

Ejecutar el script bootstrap en el servidor IIS:
```powershell
.\bootstrap\setup-iis-server.ps1 -ConfigFile .\server-config.json
```
Ver: `guia-web-deploy-iis.md` — Sección 8

---

## 8. Variables Requeridas por Entorno

Cada Variable Group debe contener estas variables:

| Variable | Tipo | Ejemplo (Test) | Ejemplo (Prod) |
|----------|------|-----------------|-----------------|
| `WebsiteName` | Texto | `MiApp-Test` | `MiApp-Prod` |
| `SiteUrl` | Texto | `https://miapp-test.empresa.local` | `https://miapp.empresa.local` |
| `WebDeployUser` | Texto | `svc_ado_deploy` | `svc_ado_deploy` |
| `WebDeployPassword` | 🔒 Secreto | `****` | `****` |

Variables adicionales según la aplicación:

| Variable | Tipo | Descripción |
|----------|------|-------------|
| `ConnectionStrings__DefaultDb` | 🔒 Secreto | Connection string de la base de datos |
| `ApiKey` | 🔒 Secreto | API key de servicios externos |
| `AppPoolName` | Texto | Nombre del App Pool en IIS |

---

## 9. Estrategia de Branching

Se usa **Git Flow simplificado**:

```
main              ← Producción (protegida, requiere PR)
  └── develop     ← Integración continua
       ├── feature/xxx
       ├── feature/yyy
       └── bugfix/zzz
```

**Flujo de CI/CD:**

| Evento | Trigger CI | Deploy Test | Deploy Prod |
|--------|-----------|-------------|-------------|
| Push a `feature/*` | No | No | No |
| PR a `develop` | Sí (Build Validation) | No | No |
| Merge a `main` | Sí (trigger automático) | Sí (automático) | Sí (con aprobación) |

---

## 10. Seguridad

### Principios Implementados

| Principio | Implementación |
|-----------|---------------|
| **Artefactos inmutables** | Se compila una vez en CI; CD descarga el mismo binario |
| **Secretos fuera del código** | Variables cifradas en Variable Groups |
| **Mínimo privilegio** | Cuenta sin admin local; deploy via WMSVC delegado |
| **SAST obligatorio** | Microsoft Security DevOps en cada build |
| **Dependency Scan** | NuGet Audit / npm audit en cada build |
| **Aprobaciones** | Prod requiere aprobación manual en el Environment |
| **Branch control** | Prod solo desde `main` (condición en YAML + check en Environment) |
| **Credenciales seguras** | WebDeployUser/Password como env vars, no en argumentos |

### Migración a Snyk

Cuando se migre de MSDO a Snyk:
1. Instalar la extensión Snyk en Azure DevOps Marketplace
2. Crear Service Connection tipo Snyk (`sc-snyk`)
3. En `steps-ci.yml`, los bloques de MSDO y Dependency Scan tienen comentarios con el código de reemplazo exacto

---

## 11. Consideraciones Especiales

### Node.js en IIS

Para alojar aplicaciones Node.js en IIS se necesita una de estas configuraciones:
- **iisnode**: módulo que permite a IIS ejecutar Node.js directamente
- **Reverse Proxy**: IIS como proxy inverso hacia un proceso Node.js (PM2, systemd)

En ambos casos, el sitio IIS necesita un `web.config` adicional. Esto se incluye en el build de la app.

### Primer Deploy

El sitio IIS **debe existir antes** del primer deploy. Web Deploy no puede crear sitios — solo sincroniza contenido. Se debe ejecutar el script bootstrap una sola vez:
```powershell
.\bootstrap\setup-iis-server.ps1 -ConfigFile .\server-config.json
```

### Health Check

Si tu aplicación no tiene un endpoint `/health`:
- Usar `healthCheckEndpoint: '/'` para verificar contra el homepage
- Considerar agregar un endpoint de health check a la aplicación (buena práctica)

### Múltiples Aplicaciones en el Mismo Servidor

Cada aplicación tiene su propio pipeline, pero comparten:
- La misma cuenta de servicio (`svc_ado_deploy`)
- Los mismos Agent Pools
- Delegación independiente por sitio IIS

### Naming del Artefacto

El artefacto se nombra: `{artifactName}-{BuildNumber}-{BranchName}`

Ejemplo: `MiApp-20260326.1-main`

Esto garantiza trazabilidad completa del binario desplegado.

---

## 12. Troubleshooting

| Problema | Causa | Solución |
|----------|-------|----------|
| `msdeploy.exe no encontrado` | Web Deploy no instalado con ADDLOCAL=ALL | Reinstalar con `ADDLOCAL=ALL` (guía-web-deploy-iis.md §2) |
| `401 Unauthorized` en deploy | Delegación no configurada | Configurar IIS Manager Permissions (guía §5) |
| `Sitio no existe en IIS` | Script bootstrap no ejecutado | Ejecutar bootstrap (guía §8) |
| Health check falla con timeout | App tarda en iniciar | Aumentar `healthCheckDelaySeconds` y `healthCheckRetries` |
| SAST falla en CI | Extensión MSDO no instalada | Instalar desde Azure DevOps Marketplace |
| `Downloads not found` en CD | Nombre de artefacto no coincide entre CI y CD | Verificar que `artifactName` sea idéntico en ambos stages |
| CD no se ejecuta | Condición de branch no cumplida | Verificar que el push sea a `main` |
| Aprobación no solicitada | Environment sin checks configurados | Agregar Approval check en el Environment |
| npm audit falla | Vulnerabilidades críticas encontradas | Revisar y parchar dependencias; `continueOnError: true` ya activo |

---

## 13. Recomendaciones y Mejoras Futuras

### 🔐 Seguridad

| Mejora | Prioridad | Descripción |
|--------|-----------|-------------|
| **Integración con Azure Key Vault** | Alta | Vincular Variable Groups a Key Vault para rotación automática de secretos sin redeploy |
| **Migración a Snyk** | Alta | Reemplazar MSDO por Snyk para SAST + SCA con mejor cobertura y dashboard centralizado |
| **Container scanning** | Media | Si se adoptan contenedores, agregar escaneo de imágenes Docker (Trivy, Snyk Container) |
| **Signed artifacts** | Media | Firmar digitalmente los artefactos para garantizar integridad end-to-end |
| **DAST (Dynamic Analysis)** | Baja | Agregar escaneo dinámico post-deploy con OWASP ZAP o Burp Suite |

### 🔧 Infraestructura y Operaciones

| Mejora | Prioridad | Descripción |
|--------|-----------|-------------|
| **Notificaciones** | Alta | Agregar notificaciones a Microsoft Teams/Slack en deploy exitoso o fallido |
| **Deployment slots / Blue-Green** | Media | Implementar zero-downtime deployments con sitios IIS A/B |
| **Smoke tests post-deploy** | Media | Agregar step de tests funcionales ligeros después del health check |
| **Métricas y dashboards** | Media | Configurar Azure DevOps Analytics + Power BI para visualizar frecuencia de deploy, lead time, MTTR |
| **Infrastructure as Code** | Baja | Automatizar creación de servidores IIS con DSC (Desired State Configuration) o Ansible |

### 📦 Pipelines

| Mejora | Prioridad | Descripción |
|--------|-----------|-------------|
| **Template validation check** | Alta | Agregar Required Template check en Environments para forzar uso de templates oficiales |
| **Multi-stage approval** | Media | Para entornos regulados, agregar gates de aprobación en Test también |
| **Deploy scheduling** | Media | Agregar Business Hours check en Prod para evitar deploys fuera de horario laboral |
| **Template para .NET Framework 4.x** | Media | Crear `steps-ci-netfx.yml` para proyectos legacy usando MSBuild |
| **Pipeline de rollback manual** | Baja | Pipeline dedicado para restaurar un backup específico sin necesidad de re-ejecutar CI |
| **Cache de dependencias** | Baja | Usar `Cache@2` task para cachear NuGet packages / node_modules y acelerar CI |
| **Code coverage reporting** | Baja | Publicar reportes de cobertura de código como artifact y tab del pipeline |

### 🏗️ Mantenibilidad

| Mejora | Prioridad | Descripción |
|--------|-----------|-------------|
| **Repositorio central de templates** | Alta | Mover templates a un repositorio dedicado y referenciar con `resources.repositories` para versionado independiente |
| **Changelog** | Media | Mantener un CHANGELOG.md para registrar cambios en los templates |
| **Schema validation** | Media | Usar `az pipelines validate` o extensiones de VS Code para validar YAML antes de commit |
| **Tests de templates** | Baja | Crear un pipeline de prueba que ejecute los templates contra un proyecto dummy para verificar que no se rompen al hacer cambios |

---

## Referencias

- [manual-azure-devops.md](../manual-azure-devops.md) — Manual completo de configuración
- [guia-web-deploy-iis.md](../guia-web-deploy-iis.md) — Guía de Web Deploy + IIS
- [Azure DevOps YAML Schema](https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema) — Referencia oficial
- [Microsoft Security DevOps](https://marketplace.visualstudio.com/items?itemName=MicrosoftSecurityDevOps.microsoft-security-devops-azdevops) — Extensión MSDO
