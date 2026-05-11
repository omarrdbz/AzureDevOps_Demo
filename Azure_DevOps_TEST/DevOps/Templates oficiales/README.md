# Templates oficiales de Azure DevOps

Templates reutilizables para CI/CD corporativo con controles de seguridad SCA, SAST y DAST. El diseño compila una vez, publica artefactos inmutables y despliega el mismo paquete en cada ambiente.

## Estructura

```text
Templates oficiales/
├── README.md
├── diagrams/
│   ├── Arquitectura.mmd
│   ├── Conexión a Snyk.mmd
│   └── Diagrama de Secuencia.mmd
├── pipelines/
│   ├── dotnet-webapp.yml
│   └── nodejs-webapp.yml
└── templates/
    ├── stage-ci.yml
    ├── stage-ci-dotnet-framework481.yml
    ├── stage-sast.yml
    ├── stage-cd.yml
    ├── stage-security-review.yml
    ├── stage-uat.yml
    ├── steps-ci.yml
    ├── steps-ci-dotnet-framework481.yml
    ├── steps-sast-fortify.yml
    └── steps-cd-iis.yml
```

## Arquitectura

El pipeline separa responsabilidades por conectividad:

| Etapa | Agente | Función | Conectividad |
|---|---|---|---|
| CI | Microsoft-hosted | Build, test, Snyk SCA, paquete Fortify y publicación de artefactos | Internet |
| SAST | Self-hosted on-prem | Envío a ScanCentral y quality gate en Fortify SSC | Red interna Fortify |
| CD Test | Self-hosted on-prem | Backup, Web Deploy, health check, rollback y DAST | IIS interno + salida a `api.probely.com:443` |
| Security Review | Environment approval | Revisión AppSec de SCA, SAST, DAST y decisión de riesgo | Azure DevOps Environment |
| UAT | Environment approval | Validación funcional/de negocio antes de producción | Azure DevOps Environment |
| CD Prod | Self-hosted on-prem | Backup, Web Deploy, health check y rollback | IIS interno |

Diagramas:

- [Arquitectura](diagrams/Arquitectura.mmd)
- [Diagrama de Secuencia](diagrams/Diagrama%20de%20Secuencia.mmd)
- [Conexión a Snyk](diagrams/Conexi%C3%B3n%20a%20Snyk.mmd)
- [Arquitectura interactiva](arquitectura-pipelines.html)

## Flujo

1. CI restaura dependencias, compila, ejecuta tests y escanea dependencias con Snyk.
2. CI genera dos artefactos: la aplicación publicada y `scancentral-pkg.zip`.
3. SAST descarga solo el paquete Fortify, lo envía a ScanCentral y evalúa Critical/High en SSC.
4. CD Test descarga la aplicación, crea backup, despliega con Web Deploy, ejecuta health check y hace rollback automático si falla.
5. CD Test actúa como scanning agent: inicia el scan en Snyk API & Web, ejecuta las pruebas DAST contra QA por red interna y reporta resultados.
6. Security Review exige revisión formal de reportes SCA, SAST y DAST por Seguridad en Aplicaciones.
7. UAT exige aceptación funcional/de negocio sobre la versión desplegada en QA/Test.
8. CD Prod despliega el mismo artefacto aprobado, condicionado a rama `main` y aprobaciones del Environment.

## Templates

### `stage-ci.yml`

Wrapper de stage para `steps-ci.yml`.

Parámetros principales:

| Parámetro | Uso |
|---|---|
| `technology` | `dotnet`, `nodejs` o `custom` |
| `vmImage` | Imagen Microsoft-hosted |
| `artifactName` | Nombre base del artefacto de aplicación |
| `enableSnykSCA` | Habilita Snyk Open Source |
| `enableNativeDependencyScan` | Habilita NuGet Audit o npm audit |
| `enableFortifyPackage` | Publica `FortifyPackage-{BuildNumber}` |

### `stage-ci-dotnet-framework481.yml`

Wrapper de stage para `steps-ci-dotnet-framework481.yml`. Usar este template para aplicaciones ASP.NET/IIS en .NET Framework 4.8.1 que requieren Visual Studio/MSBuild, `NuGetCommand@2` y `VSTest@2`, en lugar de `DotNetCoreCLI@2`.

Parámetros principales:

| Parámetro | Uso |
|---|---|
| `vmImage` | Imagen Windows con Visual Studio 2022 y .NET Framework 4.8.1 Developer Pack |
| `solutionPath` | Ruta de la solución `.sln` usada para restore/build/Fortify |
| `projectPath` | Proyecto web `.csproj` publicado como artefacto IIS |
| `buildPlatform` | Plataforma MSBuild, por ejemplo `Any CPU` |
| `vsVersion` | Versión de Visual Studio para `VSBuild@1`, por defecto `17.0` |
| `enableSnykSCA` | Habilita Snyk Open Source |
| `enableNativeDependencyScan` | Habilita NuGet Audit nativo durante restore |
| `nativeDependencySeverityThreshold` | Severidad mínima para reportar: `low`, `moderate`, `high` o `critical` |
| `nativeDependencyFailOnIssues` | Bloquea el pipeline si NuGet Audit encuentra hallazgos sobre el umbral |
| `enableFortifyPackage` | Publica `FortifyPackage-{BuildNumber}` |

### `stage-sast.yml`

Wrapper de stage para `steps-sast-fortify.yml`.

Parámetros principales:

| Parámetro | Uso |
|---|---|
| `pool` | Agent Pool self-hosted |
| `variableGroup` | Variable Group con tokens Fortify |
| `fortifyScanCentralUrl` | URL del controlador ScanCentral |
| `fortifySSCUrl` | URL de Fortify SSC |
| `fortifyAppName` | Aplicación registrada en SSC |
| `fortifyAppVersion` | Versión registrada en SSC |
| `qualityGateMaxCritical` | Máximo de Critical permitidas |
| `qualityGateMaxHigh` | Máximo de High permitidas |
| `scanTimeoutMinutes` | Timeout del scan bloqueante |

### `stage-cd.yml`

Wrapper de deployment stage para `steps-cd-iis.yml`.

Parámetros principales:

| Parámetro | Uso |
|---|---|
| `environment` | Environment de Azure DevOps |
| `pool` | Agent Pool self-hosted |
| `variableGroup` | Variables y secretos del ambiente |
| `deployPath` | Ruta física del sitio IIS |
| `backupPath` | Ruta de backups rotativos |
| `healthCheckEndpoint` | Endpoint usado para validar el despliegue |
| `enableAutoRollback` | Restaura backup si el health check falla |
| `enableSnykApiWebDAST` | Ejecuta DAST post-deploy desde el agente self-hosted |

### `stage-security-review.yml`

Stage de aprobación y evidencia para Seguridad en Aplicaciones.

Parámetros principales:

| Parámetro | Uso |
|---|---|
| `environment` | Environment que contiene aprobadores/checks AppSec |
| `pool` | Agent Pool usado para registrar evidencia mínima |
| `applicationName` | Aplicación revisada |
| `minimumBlockingSeverity` | Severidad mínima que bloquea promoción |

### `stage-uat.yml`

Stage de aprobación UAT para validación funcional/de negocio.

Parámetros principales:

| Parámetro | Uso |
|---|---|
| `environment` | Environment que contiene aprobadores/checks UAT |
| `pool` | Agent Pool usado para registrar evidencia mínima |
| `applicationName` | Aplicación validada |

## Variables

Variable Group por ambiente:

| Variable | Descripción |
|---|---|
| `WebsiteName` | Nombre del sitio en IIS |
| `SiteUrl` | URL base del sitio |
| `WebDeployUser` | Cuenta de servicio Web Deploy |
| `WebDeployPassword` | Secreto de la cuenta de servicio |
| `ProbelyApiKey` | API key de Snyk API & Web, solo si DAST está habilitado |

Variable Group Fortify:

| Variable | Descripción |
|---|---|
| `FortifyScanCentralToken` | Token para ScanCentral |
| `FortifySSCToken` | Token para Fortify SSC |

## Requisitos

CI:

- Extensión Snyk Security Scan instalada.
- Extensión Fortify instalada.
- Service Connection Snyk, por defecto `sc-snyk`.

SAST:

- Agente self-hosted con ScanCentral Client.
- Acceso al controlador ScanCentral.
- Acceso a Fortify SSC.
- Aplicación y versión creadas en Fortify SSC.

CD:

- Agente self-hosted con acceso al servidor IIS.
- Web Deploy 3.6+ y WMSVC habilitado.
- Sitio IIS creado antes del primer despliegue.
- Permisos de Web Deploy delegados a la cuenta de servicio.
- Probely CLI instalado en el agente cuando DAST esté habilitado.
- Conectividad desde el agente hacia la URL interna de QA/Test.
- Salida HTTPS 443 desde el agente hacia `api.probely.com`.

## Seguridad y resiliencia

| Control | Implementación |
|---|---|
| Artefacto inmutable | CI publica una sola vez; CD descarga el mismo artefacto |
| Secretos fuera del código | Variable Groups y variables de entorno |
| Menor privilegio | Web Deploy por WMSVC delegado |
| SCA | Snyk Open Source y escaneo nativo opcional |
| SAST | Fortify ScanCentral con quality gate |
| DAST | Snyk API & Web orquestado desde el agente self-hosted en Test |
| AppSec Review | Environment approval para revisar reportes y registrar decisión de riesgo |
| UAT | Environment approval para aceptación funcional/de negocio |
| Rollback | Backup pre-deploy y restauración automática |
| Reproducibilidad | Herramientas esperadas en el agente; no se instalan dinámicamente en runtime |
| Prod controlado | Environment approvals y condición de rama `main` |

## DAST en aplicaciones internas

Los servidores QA/Test no requieren exposición a internet. El agente self-hosted de CD actúa como scanning agent: mantiene la comunicación saliente con Snyk API & Web y ejecuta las pruebas contra la aplicación usando la red interna.

Requisitos de red:

| Origen | Destino | Uso |
|---|---|---|
| Self-hosted CD Test | `api.probely.com:443` | Orquestación, estado y resultados |
| Self-hosted CD Test | `SiteUrl` interno | Crawling, fuzzing y validaciones DAST |
| Internet | QA/Test | No requerido |

## Uso

1. Copiar uno de los ejemplos en `pipelines/`.
2. Ajustar nombres de app, rutas, pools, variable groups y URLs Fortify.
3. Crear los Variable Groups.
4. Crear los Environments `Test`, `SecurityReview`, `UAT` y `Prod`.
5. Configurar aprobaciones en `Pipelines > Environments`.
6. Asignar aprobadores AppSec en `SecurityReview` y aprobadores de negocio en `UAT`.
7. Crear el pipeline desde el YAML.

## Proceso AppSec y UAT

`SecurityReview` y `UAT` se implementan como deployment jobs para usar approvals/checks nativos de Azure DevOps Environments. El YAML registra contexto y evidencia mínima, pero la aprobación formal se configura fuera del código en el Environment.

Responsabilidades:

| Punto de control | Responsable | Evidencia |
|---|---|---|
| SecurityReview | Seguridad en Aplicaciones | Reportes SCA/SAST/DAST revisados, hallazgos triageados, riesgos aceptados o bloqueados |
| UAT | Product Owner, QA funcional o usuarios clave | Validación funcional, evidencia de aceptación y autorización de promoción |
| Prod | Release/CAB/Operaciones | Aprobación final y ventana de despliegue |

Política recomendada:

| Severidad | Acción |
|---|---|
| Critical / High | Bloquear promoción salvo excepción formal de riesgo |
| Medium | Permitir UAT si existe plan de remediación y responsable |
| Low | Registrar y atender por backlog/SLA |
| Falso positivo | Documentar justificación en la herramienta de seguridad |

Ejemplos:

- [.NET](pipelines/dotnet-webapp.yml)
- [Node.js](pipelines/nodejs-webapp.yml)

## Troubleshooting

| Problema | Revisión |
|---|---|
| `msdeploy.exe no encontrado` | Instalar Web Deploy 3.6+ en el agente/servidor |
| `401 Unauthorized` en deploy | Revisar delegación WMSVC y credenciales |
| `Sitio no existe en IIS` | Crear el sitio antes del primer despliegue |
| Health check timeout | Validar `SiteUrl`, endpoint y tiempo de arranque |
| Snyk SCA falla | Revisar extensión y Service Connection |
| ScanCentral falla | Validar cliente, controlador, token y red interna |
| Quality gate falla | Remediar Critical/High o ajustar umbrales aprobados |
| DAST falla | Validar `ProbelyApiKey`, Target ID, CLI, salida a `api.probely.com:443` y acceso interno del agente a `SiteUrl` |
| Artefacto no encontrado | Confirmar que `artifactName` coincida entre CI, SAST y CD |

## Referencias

- [Guía de Web Deploy + IIS](../guia-web-deploy-iis.md)
- [Azure DevOps YAML Schema](https://learn.microsoft.com/en-us/azure/devops/pipelines/yaml-schema)
- [Snyk Security Scan Extension](https://marketplace.visualstudio.com/items?itemName=SnykSec.snyk-security-scan)
- [Fortify Azure DevOps Extension](https://marketplace.visualstudio.com/items?itemName=fortabortext.OpenTextFortifyAzureDevOps)
