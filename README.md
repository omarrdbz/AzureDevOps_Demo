# 🌤️ WeatherApp — Práctica de Azure DevOps CI/CD

Aplicación web de clima construida con **ASP.NET Core MVC (.NET 10)** diseñada específicamente para practicar la configuración completa de **Azure DevOps**: pipelines CI/CD, Variable Groups con secretos cifrados, despliegue a IIS On-Premises, health checks y rollback automático.

---

## Tabla de Contenido

- [Descripción General](#descripción-general)
- [Tecnologías](#tecnologías)
- [Estructura del Proyecto](#estructura-del-proyecto)
- [Configuración Local](#configuración-local)
- [Manejo de Secretos](#manejo-de-secretos)
- [Páginas de la Aplicación](#páginas-de-la-aplicación)
- [Health Check](#health-check)
- [DevOps — Guía Completa](#devops--guía-completa)
  - [Arquitectura CI/CD](#arquitectura-cicd)
  - [Estructura de Pipelines YAML](#estructura-de-pipelines-yaml)
  - [Variable Groups en Azure DevOps](#variable-groups-en-azure-devops)
  - [Environments y Aprobaciones](#environments-y-aprobaciones)
  - [Agent Pools](#agent-pools)
  - [Pipeline CI — Detalle](#pipeline-ci--detalle)
  - [Pipeline CD — Detalle](#pipeline-cd--detalle)
  - [Rollback Automático](#rollback-automático)
  - [Seguridad (SAST + Dependency Scan)](#seguridad-sast--dependency-scan)
  - [Checklist de Configuración](#checklist-de-configuración)

---

## Descripción General

WeatherApp es una aplicación que muestra el clima actual y el pronóstico de los próximos días para una ciudad. Los datos se generan de forma simulada, pero la arquitectura está preparada para consumir una API externa real (como OpenWeatherMap) usando una **API Key** que se inyecta como secreto.

El propósito principal de esta aplicación es servir como **proyecto de práctica** para:

1. **Configurar un pipeline CI/CD completo** en Azure DevOps con YAML.
2. **Manejar secretos** (API keys, connection strings) usando Variable Groups cifrados.
3. **Desplegar a IIS** en servidores On-Premises con agentes self-hosted.
4. **Validar deploys** con health checks automáticos y rollback.
5. **Aplicar seguridad** con SAST y escaneo de dependencias.

---

## Tecnologías

| Componente | Tecnología |
|---|---|
| Framework | ASP.NET Core MVC — .NET 10 |
| Lenguaje | C# 14 |
| Patrón de configuración | Options Pattern (`IOptions<T>`) |
| HTTP | `IHttpClientFactory` |
| Health Checks | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| Frontend | Bootstrap 5 + Razor Views |
| CI/CD | Azure DevOps Pipelines (YAML) |
| Hosting | IIS (On-Premises) |

---

## Estructura del Proyecto

```
Azure_DevOps_TEST/
├── Controllers/
│   └── HomeController.cs           # Controlador principal (clima, config, errores)
├── Models/
│   ├── ErrorViewModel.cs           # Modelo de errores estándar
│   ├── WeatherForecast.cs          # Modelo de datos del clima
│   ├── WeatherSettings.cs          # Options pattern — mapea la sección WeatherApi
│   └── WeatherViewModel.cs         # ViewModel con estado de secretos
├── Services/
│   ├── IWeatherService.cs          # Interfaz del servicio de clima
│   └── WeatherService.cs           # Implementación (datos simulados)
├── Views/
│   ├── Home/
│   │   ├── Index.cshtml            # Dashboard de clima + estado de secretos
│   │   ├── Config.cshtml           # Vista de configuración y mapeo a Variable Groups
│   │   └── Privacy.cshtml
│   └── Shared/
│       ├── _Layout.cshtml          # Layout principal
│       └── Error.cshtml
├── Program.cs                      # Punto de entrada — registro de servicios y health checks
├── appsettings.json                # Configuración base (secretos con placeholder)
├── appsettings.Development.json    # Configuración de desarrollo local
├── Azure_DevOps_TEST.csproj        # Proyecto .NET 10
│
├── pipelines/
│   └── pipeline-dotnet-webapp.yml  # Pipeline principal (CI + CD Dev/Test/Prod)
└── templates/
    ├── ci-build.yml                # Template CI: restore → build → SAST → publish
    ├── cd-deploy-iis.yml           # Template CD: backup → deploy → health check
    ├── stages-ci.yml               # Stage wrapper CI (Microsoft-hosted)
    └── stages-cd.yml               # Stage wrapper CD (self-hosted + environment)
```

---

## Configuración Local

### Prerrequisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Ejecutar

```bash
cd Azure_DevOps_TEST
dotnet run
```

La aplicación estará disponible en `https://localhost:{puerto}`.

### Archivos de Configuración

**`appsettings.json`** — valores base con placeholders para secretos:

```json
{
  "ConnectionStrings": {
    "WeatherDb": "Server=localhost;Database=WeatherApp;Trusted_Connection=True;"
  },
  "WeatherApi": {
    "ApiKey": "LOCAL-DEV-KEY-REPLACE-IN-PIPELINE",
    "BaseUrl": "https://api.openweathermap.org/data/2.5",
    "DefaultCity": "Madrid",
    "CacheDurationMinutes": 10
  }
}
```

**`appsettings.Development.json`** — valores específicos para desarrollo local con un API key de prueba.

> ⚠️ Los valores reales de `ApiKey` y `ConnectionStrings:WeatherDb` **nunca** se guardan en el repositorio. Se inyectan desde Azure DevOps Variable Groups en tiempo de despliegue.

---

## Manejo de Secretos

La aplicación usa el **Options Pattern** de ASP.NET Core para configuración fuertemente tipada:

```csharp
// Program.cs
builder.Services.Configure<WeatherSettings>(
    builder.Configuration.GetSection(WeatherSettings.SectionName));
```

La clase `WeatherSettings` mapea la sección `WeatherApi` del `appsettings.json`:

| Propiedad | Sección en JSON | ¿Secreto? |
|---|---|---|
| `ApiKey` | `WeatherApi:ApiKey` | 🔒 Sí |
| `BaseUrl` | `WeatherApi:BaseUrl` | No |
| `DefaultCity` | `WeatherApi:DefaultCity` | No |
| `CacheDurationMinutes` | `WeatherApi:CacheDurationMinutes` | No |

Adicionalmente, la **connection string** `WeatherDb` también es un secreto.

ASP.NET Core permite sobrescribir estos valores mediante **variables de entorno** con el formato `Sección__Clave`:

| Variable de Entorno | Sobrescribe |
|---|---|
| `WeatherApi__ApiKey` | `WeatherApi:ApiKey` |
| `ConnectionStrings__WeatherDb` | `ConnectionStrings:WeatherDb` |

Esto es exactamente lo que hace Azure DevOps al inyectar variables desde un Variable Group durante el pipeline CD.

---

## Páginas de la Aplicación

| Ruta | Página | Descripción |
|---|---|---|
| `/` | **Clima** | Dashboard con clima actual, pronóstico de 5 días, buscador de ciudad y estado de los secretos (indica si vienen de Variable Groups o usan valores locales) |
| `/Home/Config` | **Configuración** | Tabla con todas las variables, valores enmascarados, y mapeo exacto a Variable Groups de Azure DevOps |
| `/health` | **Health Check** | Endpoint JSON que retorna `200 OK` — usado por el pipeline CD post-deploy |

---

## Health Check

La aplicación expone un endpoint `/health` que el pipeline CD consulta después de cada despliegue:

```csharp
// Program.cs
builder.Services.AddHealthChecks();
app.MapHealthChecks("/health");
```

```
GET /health → 200 OK (Healthy)
```

Si el health check falla tras un deploy, el pipeline ejecuta automáticamente un **rollback** al último backup válido.

---

## DevOps — Guía Completa

> 📖 Esta sección detalla toda la configuración de Azure DevOps necesaria para el pipeline CI/CD de WeatherApp. Basada en el [Manual de Configuración de Azure DevOps](Azure_DevOps_TEST/DevOps/manual-azure-devops.md).

### Arquitectura CI/CD

El proyecto sigue el principio de **separación CI/CD** con **artefactos inmutables**:

```
┌─────────────────────────────────────────────────────────────────────┐
│                        AZURE DEVOPS (Cloud)                        │
│                                                                     │
│  ┌─────────────┐    ┌──────────────┐    ┌────────────────────────┐  │
│  │  Repositorio │───▶│  Pipeline CI  │───▶│  Artefacto Versionado  │  │
│  │    (Git)     │    │  (MS-Hosted) │    │  (Pipeline Artifacts)  │  │
│  └─────────────┘    └──────────────┘    └──────────┬─────────────┘  │
│                                                     │                │
│                     ┌───────────────────────────────┼───────────┐    │
│                     │         Pipeline CD            │           │    │
│                     │                                ▼           │    │
│                     │           ┌──────┐  ┌──────────────┐      │    │
│                     │           │ Test │─▶│ Prod         │      │    │
│                     │           │      │  │ (Aprobación) │      │    │
│                     │           └──┬───┘  └──────┬───────┘      │    │
│                     └──────────────┼──────────────┼─────────────┘    │
└────────────────────────────────────┼──────────────┼──────────────────┘
                                     │              │
                            ─ ─ ─ ─ ─│─ ─ ─ ─ ─ ─ ─│─ ─  RED CORPORATIVA
                                     ▼              ▼
                              ┌──────────┐ ┌──────────┐
                              │ IIS Test │ │ IIS Prod │
                              │ (Agent)  │ │ (Agent)  │
                              └──────────┘ └──────────┘
```

**Principios clave:**

| # | Principio | Aplicación en WeatherApp |
|---|---|---|
| 1 | Separación CI/CD | CI en agentes Microsoft-hosted, CD en agentes self-hosted |
| 2 | Artefactos inmutables | Se compila una vez en CI, se despliega múltiples veces en CD |
| 3 | Mínimo privilegio | Agentes CD usan cuenta `svc_ado_deploy` sin permisos admin |
| 4 | Secretos fuera del código | API key y connection string en Variable Groups cifrados |
| 5 | Seguridad progresiva | SAST + Dependency Scan en CI, bloqueo en Producción |

---

### Estructura de Pipelines YAML

Los pipelines usan un diseño de **templates reutilizables**:

```
pipelines/
└── pipeline-dotnet-webapp.yml      ← Pipeline principal (orquestador)
        │
        ├── templates/stages-ci.yml         ← Stage wrapper CI
        │       └── templates/ci-build.yml  ← Pasos de CI
        │
        └── templates/stages-cd.yml         ← Stage wrapper CD (×2 entornos)
                └── templates/cd-deploy-iis.yml  ← Pasos de CD
```

| Template | Responsabilidad |
|---|---|
| `ci-build.yml` | Restore → Build → SAST → Dependency Scan → Publish artefacto |
| `cd-deploy-iis.yml` | Download → Backup → Stop IIS → Deploy → Config → Start IIS → Health Check |
| `stages-ci.yml` | Envuelve `ci-build.yml` con pool Microsoft-hosted |
| `stages-cd.yml` | Envuelve `cd-deploy-iis.yml` con environment, Variable Group y aprobaciones |

---

### Variable Groups en Azure DevOps

Se necesitan **2 Variable Groups** (uno por entorno) en **Pipelines → Library**:

#### `vg-weatherapp-test`

| Variable | Valor Ejemplo | ¿Secreto? |
|---|---|---|
| `ConnectionStrings__WeatherDb` | `Server=srv-test;Database=WeatherApp;...` | 🔒 Sí |
| `WeatherApi__ApiKey` | `test-openweather-api-key-xxxxx` | 🔒 Sí |
| `WeatherApi__BaseUrl` | `https://api.openweathermap.org/data/2.5` | No |
| `WeatherApi__DefaultCity` | `Madrid` | No |
| `AppPoolName` | `WeatherApp-Test` | No |
| `WebsiteName` | `WeatherApp-Test` | No |
| `DeployPath` | `C:\inetpub\wwwroot\WeatherApp` | No |
| `SiteUrl` | `https://weatherapp-test.empresa.local` | No |

#### `vg-weatherapp-prod`

Mismas variables con valores de Producción.

> 🔒 Las variables marcadas como secretas se cifran en Azure DevOps y **no se pueden leer** después de guardarlas. Solo se inyectan en tiempo de ejecución del pipeline.

**Cómo crear un Variable Group:**

1. Ir a **Pipelines → Library**
2. Clic en **"+ Variable group"**
3. Nombre: `vg-weatherapp-test`
4. Agregar cada variable de la tabla
5. Para secretos: clic en el **icono de candado** 🔒
6. **Save**

---

### Environments y Aprobaciones

Crear 2 environments en **Pipelines → Environments**:

| Environment | Aprobación Manual | Branch Control | Exclusive Lock |
|---|---|---|---|
| `Test` | ⚡ Opcional | Recomendado | ✅ Sí |
| `Prod` | ✅ **Obligatoria** | ✅ Solo `main` | ✅ Sí |

**Configurar aprobación en Prod:**

1. Ir a **Pipelines → Environments → Prod**
2. **"⋮"** → **"Approvals and checks"**
3. **"+ Add check"** → **"Approvals"**
4. Agregar al menos 2 aprobadores
5. Marcar: `Allow approvers to approve their own runs: No`

**Configurar Branch Control en Prod:**

1. **"+ Add check"** → **"Branch control"**
2. Allowed branches: `refs/heads/main`

---

### Agent Pools

| Pool | Tipo | Entorno | Servidor |
|---|---|---|---|
| *(Microsoft-hosted)* | Cloud | CI | `windows-latest` (efímero) |
| `Pool-Test` | Self-hosted | CD Test | `SRV-TEST-01` |
| `Pool-Prod` | Self-hosted | CD Prod | `SRV-PROD-01` |

Los agentes self-hosted se instalan en cada servidor IIS con la cuenta de servicio `svc_ado_deploy` (sin permisos de administrador).

---

### Pipeline CI — Detalle

**Archivo:** `templates/ci-build.yml`  
**Agente:** Microsoft-hosted (`windows-latest`)  
**Trigger:** Push a `main` o `develop` en archivos de `Azure_DevOps_TEST/`

| Paso | Task | Descripción |
|---|---|---|
| 1 | `UseDotNet@2` | Instala .NET 10 SDK |
| 2 | `DotNetCoreCLI@2 restore` | Restaura paquetes NuGet |
| 3 | `DotNetCoreCLI@2 build` | Compila en modo Release |
| 4 | `MicrosoftSecurityDevOps@1` | Escaneo SAST (credenciales en código, vulnerabilidades) |
| 5 | `DotNetCoreCLI@2 list package` | Auditoría de dependencias vulnerables |
| 6 | `DotNetCoreCLI@2 publish` | Publica la aplicación |
| 7 | `publish` | Sube el artefacto versionado: `WeatherApp-{BuildNumber}-{Branch}` |

---

### Pipeline CD — Detalle

**Archivo:** `templates/cd-deploy-iis.yml`  
**Agente:** Self-hosted (pool según entorno)

| Paso | Descripción |
|---|---|
| 1. **Download** | Descarga el artefacto inmutable generado por CI |
| 2. **Backup** | Copia el contenido actual de IIS a `C:\deploy-backups\WeatherApp\backup-{timestamp}` |
| 3. **Limpiar backups** | Retiene solo los últimos 5 backups |
| 4. **Stop IIS** | Detiene el sitio web en IIS |
| 5. **Deploy** | Copia los archivos del artefacto a la carpeta de IIS |
| 6. **Verificar config** | Confirma que `appsettings.json` existe en el deploy |
| 7. **Start IIS** | Inicia el sitio web |
| 8. **Health Check** | Consulta `GET /health` — si falla, ejecuta rollback automático |

**Flujo de stages:**

```
CI (Build) ──→ CD Test ──→ CD Prod
                              │
                         Requiere:
                         ✅ Aprobación manual
                         ✅ Branch = main
                         ✅ Stage anterior exitoso
```

---

### Rollback Automático

Si el health check falla tras un deploy, el pipeline ejecuta automáticamente:

1. Obtiene el backup más reciente de `C:\deploy-backups\WeatherApp\`
2. Detiene el sitio IIS
3. Restaura los archivos del backup
4. Reinicia el sitio IIS
5. Lanza un error para marcar el pipeline como fallido

```
C:\deploy-backups\WeatherApp\
├── backup-20260304-143022\     ← Más reciente (se restaura este)
├── backup-20260303-091500\
├── backup-20260301-160000\
├── backup-20260228-120000\
└── backup-20260225-093000\     ← Más antiguo (se elimina al llegar a 6)
```

---

### Seguridad (SAST + Dependency Scan)

El pipeline CI incluye dos gates de seguridad:

| Gate | Herramienta | Comportamiento |
|---|---|---|
| **SAST** | Microsoft Security DevOps | Detecta secretos en código, vulnerabilidades estáticas |
| **Dependency Scan** | `dotnet list package --vulnerable` | Detecta paquetes NuGet con CVEs conocidos |

**Política de bloqueo:**

| Entorno | Comportamiento ante hallazgos |
|---|---|
| Test | ⚠️ Warning — no bloquea el pipeline |
| Prod | 🛑 Bloqueo — no se despliega con vulnerabilidades altas/críticas |

> La extensión **Microsoft Security DevOps** debe instalarse desde el [Visual Studio Marketplace](https://marketplace.visualstudio.com/) en la organización de Azure DevOps.

---

### Checklist de Configuración

#### Antes de la primera ejecución

- [ ] Organización y proyecto creados en Azure DevOps (visibilidad `Private`)
- [ ] PAT generado con scope `Agent Pools (Read & manage)`
- [ ] Agent Pools creados: `Pool-Test`, `Pool-Prod`
- [ ] Agentes self-hosted instalados y **Online** en cada pool
- [ ] Variable Groups creados: `vg-weatherapp-test`, `vg-weatherapp-prod`
- [ ] Variables secretas cifradas (`ConnectionStrings__WeatherDb`, `WeatherApi__ApiKey`)
- [ ] Environments creados: `Test`, `Prod`
- [ ] Aprobación manual configurada en environment `Prod`
- [ ] Branch control configurado en `Prod` (solo `main`)
- [ ] Extensión Microsoft Security DevOps instalada
- [ ] IIS configurado en servidores destino con ASP.NET Core Hosting Bundle
- [ ] Cuenta `svc_ado_deploy` creada con permisos `Modify` en carpetas de deploy y backup
- [ ] Branch policies configuradas en `main` (reviewers, build validation)
- [ ] Pipeline creado desde `pipelines/pipeline-dotnet-webapp.yml`

#### Después de la primera ejecución exitosa

- [ ] CI compiló y publicó artefacto versionado
- [ ] SAST ejecutado sin errores bloqueantes
- [ ] Artefacto desplegado en Test — health check pasó
- [ ] Deploy a Prod esperando aprobación (no auto-deploy)
- [ ] Página `/Home/Config` muestra secretos inyectados desde Variable Groups
- [ ] Endpoint `/health` responde `200 OK`

---

*Proyecto basado en el [Manual de Configuración de Azure DevOps](Azure_DevOps_TEST/DevOps/manual-azure-devops.md) — Estándar Corporativo de Pipelines.*
