# Plantillas Oficiales de Pipelines — Azure DevOps

**Versión:** 2.0
**Fecha:** Abril 2026
**Enfoque:** CI/CD corporativo con seguridad híbrida (Fortify ScanCentral + Snyk)

---

## Tabla de Contenido

1. [Estructura de Archivos](#1-estructura-de-archivos)
2. [Arquitectura Híbrida de Seguridad](#2-arquitectura-híbrida-de-seguridad)
3. [Flujo de Ejecución (4 Stages)](#3-flujo-de-ejecución-4-stages)
4. [Descripción de Templates](#4-descripción-de-templates)
5. [Dependencias entre Templates](#5-dependencias-entre-templates)
6. [Requisitos por Etapa](#6-requisitos-por-etapa)
7. [Cómo Adaptar a un Nuevo Proyecto](#7-cómo-adaptar-a-un-nuevo-proyecto)
8. [Variables Requeridas por Entorno](#8-variables-requeridas-por-entorno)
9. [Estrategia de Branching](#9-estrategia-de-branching)
10. [Seguridad](#10-seguridad)
11. [Integración con Fortify ScanCentral](#11-integración-con-fortify-scancentral)
12. [Integración con Snyk (SCA y DAST)](#12-integración-con-snyk-sca-y-dast)
13. [Consideraciones Especiales](#13-consideraciones-especiales)
14. [Troubleshooting](#14-troubleshooting)
15. [Recomendaciones y Mejoras Futuras](#15-recomendaciones-y-mejoras-futuras)

---

## 1. Estructura de Archivos

```
Templates oficiales/
├── README.md                              ← Este archivo
├── templates/
│   ├── steps-ci.yml                       ← Steps: build, test, Snyk SCA, Fortify package
│   ├── steps-sast-fortify.yml             ← Steps: ScanCentral upload + SSC quality gate
│   ├── steps-cd-iis.yml                   ← Steps: backup, deploy IIS, health check, Snyk DAST
│   ├── stage-ci.yml                       ← Stage wrapper CI (Microsoft-hosted)
│   ├── stage-sast.yml                     ← Stage wrapper SAST (self-hosted on-prem)
│   └── stage-cd.yml                       ← Stage wrapper CD (self-hosted on-prem)
└── pipelines/
    ├── dotnet-webapp.yml                  ← Ejemplo completo: app .NET
    └── nodejs-webapp.yml                  ← Ejemplo completo: app Node.js
```

---

## 2. Arquitectura Híbrida de Seguridad

El flujo de seguridad está diseñado para resolver la restricción de que **Fortify ScanCentral está on-premise SIN salida a internet**, mientras que **Snyk es SaaS y requiere internet**.

```
┌─────────────────────────────────────────────────────────────────┐
│  AGENTE MICROSOFT-HOSTED (CON internet)                        │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Snyk SCA → escanea dependencias via snyk.io             │  │
│  │  Fortify ScanCentral package → empaqueta código offline   │  │
│  └───────────────────────────────────────────────────────────┘  │
│                         │ artefactos                             │
│                         ▼ (via Azure DevOps)                    │
│  AGENTE SELF-HOSTED ON-PREM (SIN internet)                     │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  ScanCentral start → envía paquete al Controller on-prem │  │
│  │  SSC Quality Gate → evalúa vulnerabilidades               │  │
│  └───────────────────────────────────────────────────────────┘  │
│                         │                                       │
│                         ▼                                       │
│  AGENTE SELF-HOSTED ON-PREM (acceso IIS + api.probely.com:443)        │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  WebDeploy → despliega a IIS                              │  │
│  │  Probely DAST → escaneo dinámico post-deploy                │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

| Herramienta | Tipo | Dónde corre | Requiere Internet |
|-------------|------|-------------|-------------------|
| **Snyk SCA** | SCA (dependencias) | CI — MS-hosted | ✅ Sí |
| **Fortify ScanCentral** | SAST (código) | SAST — Self-hosted | ❌ No |
| **Snyk API & Web / Probely** | DAST (dinámico) | CD — Self-hosted | ✅ Sí (api.probely.com:443) |

---

## 3. Flujo de Ejecución (4 Stages)

```
┌─────────────────────────────────────────────────────────────────────┐
│  STAGE 1: CI — Build, Test, SCA & Fortify Package                  │
│  Pool: Microsoft-hosted (efímero, CON internet)                    │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │  1. Setup (instalar SDK/runtime)                              │  │
│  │  2. Build (compilar/transpilar)                               │  │
│  │  3. Test (ejecutar tests unitarios)                           │  │
│  │  4. Snyk SCA (análisis de dependencias)                       │  │
│  │  4.5 NuGet Audit / npm audit (alternativa nativa)             │  │
│  │  5. Fortify ScanCentral package (empaquetado offline)         │  │
│  │  6. Publish artefacto app + scancentral-pkg.zip               │  │
│  └──────────────────────────────────────────────────────────────┘  │
└──────────────────────────┬─────────────────────────────────────────┘
                           │ 2 artefactos
┌──────────────────────────▼─────────────────────────────────────────┐
│  STAGE 2: SAST — Fortify ScanCentral Upload & Quality Gate         │
│  Pool: Self-hosted On-Prem (SIN internet, CON acceso Fortify)      │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │  1. Download scancentral-pkg.zip                              │  │
│  │  2. Submit scan al Controller ScanCentral                     │  │
│  │  3. Esperar resultados (block mode)                           │  │
│  │  4. Quality Gate contra Fortify SSC                           │  │
│  └──────────────────────────────────────────────────────────────┘  │
└──────────────────────────┬─────────────────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────────────────┐
│  STAGE 3: CD_Test — Deploy + Snyk DAST                             │
│  Pool: Self-hosted (Pool-Test, acceso IIS + api.probely.com:443)       │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │  1. Download artefacto app                                    │  │
│  │  2. Backup pre-deploy (+ rotación)                            │  │
│  │  3. Verificar sitio IIS                                       │  │
│  │  4. Deploy via Web Deploy (msdeploy.exe)                      │  │
│  │  5. Health Check (+ auto-rollback si falla)                   │  │
│  │  6. Snyk DAST (escaneo dinámico post-deploy)                  │  │
│  │  7. Deploy Summary                                            │  │
│  └──────────────────────────────────────────────────────────────┘  │
└──────────────────────────┬─────────────────────────────────────────┘
                           │
┌──────────────────────────▼─────────────────────────────────────────┐
│  STAGE 4: CD_Prod — Deploy a Producción  ⚠️ REQUIERE APROBACIÓN   │
│  Pool: Self-hosted (Pool-Prod)                                     │
│  Condición: Solo desde rama main                                   │
│  (mismos steps que CD_Test, sin DAST)                              │
└────────────────────────────────────────────────────────────────────┘
```

---

## 4. Descripción de Templates

### `templates/steps-ci.yml` — Steps de CI

**Propósito:** Compilar, ejecutar tests, Snyk SCA, empaquetar para Fortify, publicar 2 artefactos.

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
| `enableSnykSCA` | boolean | `true` | Habilitar Snyk SCA (dependencias) |
| `snykServiceConnection` | string | `sc-snyk` | Service Connection de Snyk |
| `snykFailOnIssues` | boolean | `false` | Fallar pipeline si Snyk encuentra issues |
| `enableNativeDependencyScan` | boolean | `true` | NuGet Audit / npm audit como alternativa |
| `enableFortifyPackage` | boolean | `true` | Empaquetar código para ScanCentral |
| `fortifyBuildId` | string | — | ID del build para ScanCentral |
| `prePublishSteps` | stepList | `[]` | Steps custom antes de publicar |

---

### `templates/steps-sast-fortify.yml` — Steps de SAST

**Propósito:** Enviar paquete ScanCentral al controlador on-prem y evaluar Quality Gate contra SSC.

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `artifactName` | string | `WebApp` | Debe coincidir con CI |
| `fortifyScanCentralUrl` | string | — | URL del controlador ScanCentral |
| `fortifyScanCentralToken` | string | — | Token de autenticación (secreto) |
| `fortifySSCUrl` | string | — | URL del servidor Fortify SSC |
| `fortifySSCAuthToken` | string | — | Token de SSC (secreto) |
| `fortifyAppName` | string | — | Nombre de la app en SSC |
| `fortifyAppVersion` | string | — | Versión en SSC |
| `fortifyBuildId` | string | — | ID del build |
| `qualityGateEnabled` | boolean | `true` | Evaluar quality gate |
| `qualityGateMaxCritical` | number | `0` | Máximo Critical (0=cero tolerancia) |
| `qualityGateMaxHigh` | number | `0` | Máximo High (0=cero tolerancia) |
| `scanTimeoutMinutes` | number | `60` | Timeout del scan |
| `pollIntervalSeconds` | number | `30` | Intervalo de polling |

---

### `templates/steps-cd-iis.yml` — Steps de CD (IIS + Snyk DAST)

**Propósito:** Backup, deploy via Web Deploy, health check, auto-rollback, y Snyk DAST.

| Parámetro | Tipo | Default | Descripción |
|-----------|------|---------|-------------|
| `artifactName` | string | `WebApp` | Debe coincidir con el de CI |
| `deployPath` | string | `C:\inetpub\wwwroot\WebApp` | Ruta física del sitio IIS |
| `backupPath` | string | `C:\deploy-backups\WebApp` | Carpeta de backups rotativos |
| `msdeployPath` | string | `C:\Program Files\IIS\...` | Ruta a msdeploy.exe |
| `healthCheckEndpoint` | string | `/health` | Endpoint de verificación |
| `healthCheckRetries` | number | `3` | Reintentos de health check |
| `healthCheckDelaySeconds` | number | `10` | Segundos entre reintentos |
| `enableAutoRollback` | boolean | `true` | Rollback automático si falla HC |
| `maxBackups` | number | `5` | Máximo de backups a retener |
| `enableSnykApiWebDAST` | boolean | `false` | Habilitar Snyk API & Web DAST (Probely) |
| `snykApiWebTargetId` | string | — | Target ID en Snyk API & Web |
| `snykApiWebScanProfile` | string | `normal` | `lightning` \| `normal` \| `safe` |
| `snykApiWebTimeoutMinutes` | number | `60` | Timeout del scan DAST |
| `snykApiWebSeverityThreshold` | string | `HIGH` | `LOW` \| `MEDIUM` \| `HIGH` \| `CRITICAL` |
| `snykApiWebFailOnIssues` | boolean | `true` | Fallar si DAST encuentra >= umbral |
| `environment` | string | — | Nombre del entorno (informativo) |

**Variables requeridas en el Variable Group:**
- `WebsiteName` — Nombre del sitio en IIS
- `SiteUrl` — URL completa del sitio
- `WebDeployUser` — Cuenta de servicio
- `WebDeployPassword` 🔒 — Secreto
- `ProbelyApiKey` 🔒 — API Key JWT de Snyk API & Web / Probely (si DAST habilitado)

> **NOTA:** `ProbelyApiKey` es una credencial **distinta** a la Service Connection de Snyk SCA.
> Son productos Snyk separados con tokens propios. No reutilizar.

---

### Stage Wrappers

| Template | Pool | Propósito |
|----------|------|-----------|
| `stage-ci.yml` | `vmImage` (MS-hosted) | Wrapper para steps-ci.yml |
| `stage-sast.yml` | `name` (Self-hosted) | Wrapper para steps-sast-fortify.yml |
| `stage-cd.yml` | `name` (Self-hosted) | Wrapper para steps-cd-iis.yml |

---

## 5. Dependencias entre Templates

```
Pipeline (.yml del proyecto)
│
├──► stage-ci.yml
│    └──► steps-ci.yml
│
├──► stage-sast.yml
│    └──► steps-sast-fortify.yml
│
├──► stage-cd.yml (Test)
│    └──► steps-cd-iis.yml
│
└──► stage-cd.yml (Prod)
     └──► steps-cd-iis.yml
```

**Reglas de dependencia entre stages:**
- `SAST` depende de `CI`
- `CD_Test` depende de `SAST`
- `CD_Prod` depende de `CD_Test`

---

## 6. Requisitos por Etapa

### Stage CI ✅

| Requisito | Dónde configurar |
|-----------|-----------------|
| Extensión Snyk Security Scan instalada | Azure DevOps Marketplace |
| Extensión Fortify instalada | Azure DevOps Marketplace |
| Service Connection Snyk (`sc-snyk`) | Project Settings > Service Connections |
| SDK de la tecnología disponible | Automático (MS-hosted) |

### Stage SAST ✅

| Requisito | Dónde configurar |
|-----------|-----------------|
| Agent Pool self-hosted con agente Online | Azure DevOps > Agent Pools |
| ScanCentral Client instalado en agente | Agente on-prem |
| Controlador ScanCentral accesible | Red interna |
| Fortify SSC accesible | Red interna |
| Aplicación y versión creadas en SSC | Fortify SSC |

### Stage CD ✅

| Requisito | Dónde configurar |
|-----------|-----------------|
| Agent Pool self-hosted con agente Online | Azure DevOps > Agent Pools |
| Web Deploy 3.6+ instalado | `guia-web-deploy-iis.md` §2 |
| WMSVC habilitado y corriendo | `guia-web-deploy-iis.md` §4 |
| Sitio IIS creado (via bootstrap) | `guia-web-deploy-iis.md` §8 |
| Variable Group con variables del entorno | Pipelines > Library |
| Environment con aprobaciones (Prod) | Pipelines > Environments |
| Salida a api.probely.com:443 (si DAST habilitado) | Firewall |

---

## 7. Cómo Adaptar a un Nuevo Proyecto

### Paso 1: Copiar el pipeline ejemplo

```bash
# Para .NET:
cp "DevOps/Templates oficiales/pipelines/dotnet-webapp.yml" "pipelines/mi-proyecto.yml"

# Para Node.js:
cp "DevOps/Templates oficiales/pipelines/nodejs-webapp.yml" "pipelines/mi-proyecto.yml"
```

### Paso 2: Reemplazar los placeholders

Buscar todos los `<!-- CAMBIAR -->` y reemplazar con valores de tu proyecto.

### Paso 3: Crear recursos en Azure DevOps

1. **Variable Groups** en Pipelines > Library:
   - `vg-miproyecto-test` / `vg-miproyecto-prod` (WebDeploy + Snyk)
   - `vg-fortify` (tokens de ScanCentral y SSC — compartido)

2. **Environments** en Pipelines > Environments:
   - `Test` y `Prod` con aprobaciones

3. **Agent Pools**: deben existir con agentes online

### Paso 4: Crear el pipeline en Azure DevOps

Pipelines > New Pipeline > Existing YAML > seleccionar tu archivo

---

## 8. Variables Requeridas por Entorno

### Variable Group del entorno (por app)

| Variable | Tipo | Ejemplo |
|----------|------|---------|
| `WebsiteName` | Texto | `MiApp-Test` |
| `SiteUrl` | Texto | `https://miapp-test.empresa.local` |
| `WebDeployUser` | Texto | `svc_ado_deploy` |
| `WebDeployPassword` | 🔒 | `****` |
| `ProbelyApiKey` | 🔒 | `****` (API Key de Snyk API & Web / Probely — solo si DAST habilitado) |

### Variable Group de Fortify (compartido)

| Variable | Tipo | Descripción |
|----------|------|-------------|
| `FortifyScanCentralToken` | 🔒 | Token para el controlador ScanCentral |
| `FortifySSCToken` | 🔒 | Token para la API de Fortify SSC |

---

## 9. Estrategia de Branching

Se usa **Git Flow simplificado**:

| Evento | CI | SAST | CD Test | CD Prod |
|--------|-----|------|---------|---------|
| Push a `feature/*` | No | No | No | No |
| PR a `develop` | Sí (Build Validation) | No | No | No |
| Merge a `main` | Sí | Sí | Sí (auto) | Sí (con aprobación) |

---

## 10. Seguridad

### Principios Implementados

| Principio | Implementación |
|-----------|---------------|
| **Artefactos inmutables** | Se compila una vez en CI; CD descarga el mismo binario |
| **Secretos fuera del código** | Variables cifradas en Variable Groups |
| **Mínimo privilegio** | Cuenta sin admin local; deploy via WMSVC delegado |
| **SCA obligatorio** | Snyk Open Source en cada build (CI) |
| **SAST obligatorio** | Fortify ScanCentral con Quality Gate (SAST stage) |
| **DAST post-deploy** | Snyk API & Web / Probely DAST contra app desplegada (CD Test) |
| **Quality Gate** | Pipeline falla si Critical > 0 o High > 0 |
| **Aprobaciones** | Prod requiere aprobación manual |
| **Branch control** | Prod solo desde `main` |

### Stack de Seguridad

| Capa | Herramienta | Tipo | Ubicación |
|------|-------------|------|-----------|
| Dependencias | **Snyk Open Source** | SCA | CI (MS-hosted) |
| Dependencias | NuGet Audit / npm audit | SCA nativo | CI (alternativa) |
| Código fuente | **Fortify ScanCentral** | SAST | SAST (Self-hosted) |
| App desplegada | **Snyk API & Web / Probely** | DAST | CD (Self-hosted) |

---

## 11. Integración con Fortify ScanCentral

### Estrategia: Empaquetado Offline

El código se **empaqueta en CI** (agente con internet) y se **escanea en SAST** (agente on-prem sin internet). El paquete viaja como artefacto del pipeline.

### Requisitos

| Requisito | Descripción |
|-----------|-------------|
| **Extensión Fortify** | Instalada desde Azure DevOps Marketplace |
| **ScanCentral Client** | Provisto por la extensión Fortify |
| **ScanCentral Controller** | Accesible desde agente on-prem |
| **Fortify SSC** | Accesible desde agente on-prem (para quality gate) |
| **App + Versión en SSC** | Creadas previamente en Fortify SSC |

### Configuración

1. Instalar extensión Fortify en Azure DevOps Marketplace
2. Crear aplicación y versión en Fortify SSC
3. Generar tokens (ScanCentral + SSC) y guardar en Variable Group `vg-fortify`
4. Configurar parámetros en el pipeline (ver ejemplos en `pipelines/`)

### Quality Gate

Criterios por defecto (recomendados):

| Severidad | Umbral | Acción |
|-----------|--------|--------|
| **Critical** | 0 | Pipeline **falla** |
| **High** | 0 | Pipeline **falla** |
| **Medium** | ∞ | Warning (pipeline continúa) |
| **Low** | ∞ | Warning (pipeline continúa) |

Configurable via `qualityGateMaxCritical` y `qualityGateMaxHigh`.

---

## 12. Integración con Snyk (SCA y DAST)

### Snyk SCA (Stage CI)

Escanea dependencias de terceros en busca de vulnerabilidades conocidas.

**Requisitos:**
1. Extensión "Snyk Security Scan" instalada
2. Service Connection tipo Snyk (`sc-snyk`)
3. Agente MS-hosted con salida a internet

### Snyk API & Web DAST — Probely (Stage CD)

Escaneo dinámico real (DAST) contra la aplicación desplegada y corriendo.
Usa el motor Probely (adquirido por Snyk) a través de su CLI/API.

> **IMPORTANTE:** Esto NO es `snyk test` (que es SCA para dependencias).
> `snyk test --target-reference=<URL>` **no es DAST** — solo agrupa resultados por rama.

**Requisitos:**
1. Probely CLI instalada en agente on-prem: `pip install probely`
2. Target ID del sitio registrado en Snyk API & Web
3. API Key JWT de Snyk API & Web (`ProbelyApiKey`) — **distinta** al token SCA
4. Salida a `api.probely.com:443` desde agente on-prem
5. Variable `ProbelyApiKey` 🔒 en el Variable Group del entorno

### Uso

```yaml
# SCA en CI (Snyk Open Source):
enableSnykSCA: true
snykServiceConnection: 'sc-snyk'

# DAST en CD (Snyk API & Web / Probely — solo Test, no Prod):
enableSnykApiWebDAST: true               # Stage Test
snykApiWebTargetId: '<TARGET_ID>'
snykApiWebSeverityThreshold: 'HIGH'
snykApiWebFailOnIssues: true

enableSnykApiWebDAST: false              # Stage Prod
```

---

## 13. Consideraciones Especiales

### Node.js en IIS

Se requiere iisnode o IIS como reverse proxy hacia PM2. El deploy via Web Deploy sincroniza archivos igual que .NET.

### Primer Deploy

El sitio IIS debe existir antes del primer deploy. Ejecutar bootstrap:
```powershell
.\bootstrap\setup-iis-server.ps1 -ConfigFile .\server-config.json
```

### Naming de Artefactos

| Artefacto | Formato | Ejemplo |
|-----------|---------|---------|
| App | `{name}-{build}-{branch}` | `MiApp-20260430.1-main` |
| Fortify Package | `FortifyPackage-{build}` | `FortifyPackage-20260430.1` |

---

## 14. Troubleshooting

| Problema | Causa | Solución |
|----------|-------|----------|
| `msdeploy.exe no encontrado` | Web Deploy no instalado | Reinstalar con `ADDLOCAL=ALL` |
| `401 Unauthorized` en deploy | Delegación no configurada | Configurar IIS Manager Permissions |
| `Sitio no existe en IIS` | Bootstrap no ejecutado | Ejecutar bootstrap |
| Health check timeout | App tarda en iniciar | Aumentar `healthCheckDelaySeconds` |
| Snyk SCA falla | Service Connection no creado | Crear SC tipo Snyk en Project Settings |
| ScanCentral package falla | Extensión Fortify no instalada | Instalar desde Marketplace |
| ScanCentral submit falla | Controller inaccesible | Verificar red agente → controller |
| Quality Gate falla | Vulnerabilidades Critical/High | Remediar en código o ajustar umbrales |
| SSC API error | Token inválido o SSC inaccesible | Regenerar token en SSC |
| Snyk DAST falla | Sin salida a api.probely.com:443 | Habilitar en firewall |
| `Downloads not found` en CD | Nombre artefacto no coincide | Verificar `artifactName` idéntico |

---

## 15. Recomendaciones y Mejoras Futuras

| Mejora | Prioridad | Descripción |
|--------|-----------|-------------|
| **Azure Key Vault** | Alta | Vincular Variable Groups a Key Vault |
| **Notificaciones Teams** | Alta | Alertas de deploy y quality gate |
| **Template validation check** | Alta | Required Template check en Environments |
| **Snyk Container** | Media | Escaneo de imágenes Docker si se adoptan contenedores |
| **Blue-Green deploys** | Media | Zero-downtime con sitios IIS A/B |
| **Smoke tests post-deploy** | Media | Tests funcionales después del health check |
| **Repo central de templates** | Alta | `resources.repositories` para versionado independiente |
| **Cache de dependencias** | Baja | `Cache@2` para NuGet/node_modules |

---

## Referencias

- [guia-web-deploy-iis.md](../guia-web-deploy-iis.md) — Guía de Web Deploy + IIS
- [Azure DevOps YAML Schema](https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema)
- [Snyk Security Scan Extension](https://marketplace.visualstudio.com/items?itemName=SnykSec.snyk-security-scan)
- [Fortify Extension](https://marketplace.visualstudio.com/items?itemName=fortabortext.OpenTextFortifyAzureDevOps)
