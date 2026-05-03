# Manual de Configuración de Azure DevOps — Desde Cero

**Versión:** 1.0  
**Fecha:** Marzo 2026  
**Basado en:** Estándar de Pipelines (especificaciones.md)  
**Enfoque:** On-Prem + IIS + .NET / .NET Framework + Control de Costos + Seguridad Baseline

---

## Tabla de Contenido

1. [Introducción y Arquitectura](#1-introducción-y-arquitectura)
2. [Prerrequisitos Generales](#2-prerrequisitos-generales)
3. [Creación de Organización y Proyecto](#3-creación-de-organización-y-proyecto)
4. [Personal Access Tokens (PATs)](#4-personal-access-tokens-pats)
5. [Agent Pools](#5-agent-pools)
6. [Instalación del Agente Self-Hosted (CD)](#6-instalación-del-agente-self-hosted-cd)
7. [Service Connections](#7-service-connections)
8. [Variable Groups y Secretos](#8-variable-groups-y-secretos)
9. [Environments y Approvals](#9-environments-y-approvals)
10. [Repositorios y Branching](#10-repositorios-y-branching)
11. [Pipelines — Creación desde YAML](#11-pipelines--creación-desde-yaml)
12. [Proyectos .NET Core / .NET 5+](#12-proyectos-net-core--net-5)
13. [Proyectos Legacy .NET Framework 4.x](#13-proyectos-legacy-net-framework-4x)
14. [Gates de Seguridad (SAST + Dependency Scan)](#14-gates-de-seguridad-sast--dependency-scan)
15. [IIS — Preparación del Servidor Destino](#15-iis--preparación-del-servidor-destino)
16. [Rollback y Backup](#16-rollback-y-backup)
17. [Auditoría y Observabilidad](#17-auditoría-y-observabilidad)
18. [Rotación de PATs y Secretos](#18-rotación-de-pats-y-secretos)
19. [Troubleshooting Común](#19-troubleshooting-común)
20. [Roadmap de Seguridad](#20-roadmap-de-seguridad)
21. [Apéndice A — Estructura de Archivos YAML](#apéndice-a--estructura-de-archivos-yaml)
22. [Apéndice B — Checklist de Verificación](#apéndice-b--checklist-de-verificación)

---

## 1. Introducción y Arquitectura

### 1.1 Propósito

Este manual guía paso a paso la configuración completa de Azure DevOps para un entorno con infraestructura On-Premises, aplicaciones .NET desplegadas en IIS, y un modelo CI/CD seguro, auditable y de bajo costo.

### 1.2 Principios Obligatorios

Toda configuración debe respetar estos principios sin excepción:

| # | Principio | Descripción |
|---|-----------|-------------|
| 1 | **Separación CI/CD** | CI en agentes Microsoft-hosted (efímeros). CD en agentes self-hosted dedicados. |
| 2 | **Artefactos Inmutables** | Se construye **una sola vez**, se despliega **múltiples veces**. |
| 3 | **Mínimo Privilegio** | Agentes sin permisos admin. Cuentas técnicas dedicadas. |
| 4 | **Secretos Fuera del Código** | Prohibido secretos en repositorios. Solo Variable Groups cifrados o Key Vault. |
| 5 | **Seguridad Progresiva** | SAST + Dependency Scan obligatorios. Bloqueo en Producción. |

### 1.3 Modelo Arquitectónico

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
│                     │  ┌──────┐  ┌──────┐  ┌──────────────┐     │    │
│                     │  │ Dev  │─▶│ Test │─▶│ Prod         │     │    │
│                     │  │      │  │      │  │ (Aprobación) │     │    │
│                     │  └──┬───┘  └──┬───┘  └──────┬───────┘     │    │
│                     └─────┼────────┼──────────────┼─────────────┘    │
└───────────────────────────┼────────┼──────────────┼──────────────────┘
                            │        │              │
                   ─ ─ ─ ─ ─│─ ─ ─ ─│─ ─ ─ ─ ─ ─ ─│─ ─  RED CORPORATIVA
                            ▼        ▼              ▼
                     ┌──────────┐ ┌──────────┐ ┌──────────┐
                     │ IIS Dev  │ │ IIS Test │ │ IIS Prod │
                     │ (Agent)  │ │ (Agent)  │ │ (Agent)  │
                     └──────────┘ └──────────┘ └──────────┘
```

### 1.4 Tabla de Componentes

| Componente | Responsabilidad | Ubicación |
|---|---|---|
| **CI Pipeline** | Compilar, probar, escanear, publicar artefacto | Microsoft-hosted agent (Cloud) |
| **CD Pipeline** | Descargar artefacto, inyectar secretos, deploy, health check | Self-hosted agent (On-Prem) |
| **Servidor IIS** | Recibir y servir la aplicación | On-Premises (VM / físico) |
| **Variable Groups** | Almacenar secretos cifrados por entorno | Azure DevOps Library |
| **Environments** | Controlar gates, aprobaciones y auditoría | Azure DevOps Environments |

> ⚠️ **REGLA CRÍTICA:** El servidor IIS **NUNCA** compila. Solo recibe artefactos firmados/versionados.

---

## 2. Prerrequisitos Generales

Antes de comenzar, asegúrate de contar con lo siguiente:

### 2.1 Cuentas y Accesos

- [ ] Cuenta de Microsoft o Azure AD con acceso a [dev.azure.com](https://dev.azure.com)
- [ ] Permisos de **Project Collection Administrator** o **Organization Owner**
- [ ] Acceso administrativo al servidor(es) On-Premises donde se instalarán los agentes

### 2.2 Software en la Máquina del Administrador

- [ ] Navegador web moderno (Edge, Chrome)
- [ ] PowerShell 5.1+ (incluido en Windows Server 2016+)
- [ ] (Opcional) Azure CLI: `winget install Microsoft.AzureCLI`

### 2.3 Infraestructura On-Premises

- [ ] Windows Server 2016+ con IIS habilitado
- [ ] Conectividad de red hacia `https://dev.azure.com` y `https://vstsagentpackage.azureedge.net`
- [ ] Cuenta de servicio técnica creada (`svc_ado_deploy`) — ver sección 6
- [ ] Puertos 443 (HTTPS) abiertos en firewall hacia Azure DevOps

### 2.4 Verificación de Conectividad

Desde el servidor On-Prem, ejecutar en PowerShell:

```powershell
# Verificar conectividad a Azure DevOps
Test-NetConnection -ComputerName dev.azure.com -Port 443

# Verificar conectividad al CDN de agentes
Test-NetConnection -ComputerName vstsagentpackage.azureedge.net -Port 443

# Verificar resolución DNS
Resolve-DnsName dev.azure.com
```

> ✅ **Checkpoint:** Ambas pruebas deben mostrar `TcpTestSucceeded: True`.

---

## 3. Creación de Organización y Proyecto

### 3.1 Crear la Organización

1. Navegar a **https://dev.azure.com**
2. Iniciar sesión con la cuenta corporativa (Azure AD preferido)
3. Clic en **"New organization"**
4. Ingresar nombre de la organización (ej: `miempresa-devops`)
5. Seleccionar la región más cercana a la infraestructura On-Prem
6. Clic en **"Continue"**

> 📋 **Convención de Nombres:** Usar formato `empresa-unidad` (ej: `acme-desarrollo`).  
> La región afecta la latencia de los agentes towards Azure DevOps. Preferir la misma región geográfica de los servidores.

### 3.2 Crear el Proyecto

1. En la página principal de la organización, clic en **"+ New project"**
2. Configurar:

| Campo | Valor Recomendado |
|---|---|
| **Project name** | Nombre descriptivo (ej: `MiAplicacionWeb`) |
| **Description** | Breve descripción del proyecto |
| **Visibility** | `Private` (obligatorio para proyectos corporativos) |
| **Version control** | `Git` |
| **Work item process** | `Agile`, `Scrum` o `Basic` según metodología del equipo |

3. Clic en **"Create"**

### 3.3 Configuración Inicial de Permisos del Proyecto

1. Ir a **Project Settings** (engranaje inferior izquierdo)
2. En **General → Overview**:
   - Verificar que la visibilidad sea `Private`
3. En **Permissions**:
   - Grupo `Contributors`: Desarrolladores del equipo
   - Grupo `Build Administrators`: Encargados de pipelines
   - Grupo `Project Administrators`: Líderes técnicos / DevOps
4. En **Repos → Repositories → Security**:
   - Deshabilitar `Allow` de eliminación de ramas para `Contributors`

> ✅ **Checkpoint:** El proyecto aparece en la página principal de la organización con visibilidad `Private`.

---

## 4. Personal Access Tokens (PATs)

### 4.1 ¿Qué es un PAT?

Un Personal Access Token es una credencial alternativa a la contraseña que permite autenticar operaciones contra Azure DevOps. Se usa principalmente para:

- Registrar agentes self-hosted
- Acceso programático a APIs
- Integración con herramientas externas

### 4.2 Scopes Necesarios

Se necesitan PATs con los siguientes scopes mínimos:

| PAT | Uso | Scopes Requeridos | Expiración |
|---|---|---|---|
| **PAT-Agent** | Registrar agentes self-hosted | `Agent Pools (Read & manage)` | 90 días máx. |
| **PAT-Pipeline** | Ejecutar pipelines (si es necesario) | `Build (Read & execute)`, `Code (Read)` | 90 días máx. |
| **PAT-Admin** | Administración general (usar raramente) | `Full access` | 30 días máx. |

### 4.3 Crear un PAT — Paso a Paso

1. En Azure DevOps, clic en el **icono de usuario** (esquina superior derecha)
2. Seleccionar **"Personal access tokens"**
3. Clic en **"+ New Token"**
4. Configurar:

```
Name:           PAT-Agent-Prod (nombre descriptivo)
Organization:   miempresa-devops (seleccionar la correcta)
Expiration:     Custom defined → 90 days (máximo recomendado)
Scopes:         Custom defined
                → Agent Pools: Read & manage ✓
```

5. Clic en **"Create"**
6. **¡COPIAR EL TOKEN INMEDIATAMENTE!** — No se puede recuperar después

### 4.4 Almacenamiento Seguro de PATs

> 🔴 **PROHIBIDO:** Guardar PATs en repositorios, archivos de texto, correos electrónicos o chats.

**Métodos aceptados:**

| Método | Cuándo Usar |
|---|---|
| Azure Key Vault | Ideal — acceso auditado y rotación automática |
| Gestor de contraseñas corporativo | Aceptable — (ej: CyberArk, 1Password Business) |
| Variable Group cifrado en Azure DevOps | Solo para PATs de pipeline, no de agentes |
| Windows Credential Manager | Temporal — solo para registro inicial de agentes |

### 4.5 Rotación de PATs

La rotación es **obligatoria** según la siguiente cadencia:

| Tipo de PAT | Frecuencia de Rotación |
|---|---|
| PAT-Agent | Cada 90 días |
| PAT-Pipeline | Cada 90 días |
| PAT-Admin | Cada 30 días o después de cada uso |

**Proceso de rotación:**

1. Crear nuevo PAT con los mismos scopes
2. Actualizar el agente o servicio que usa el PAT anterior
3. Verificar funcionamiento con el nuevo PAT
4. Revocar el PAT anterior

### 4.6 Revocar un PAT

1. Ir a **User Settings → Personal access tokens**
2. Localizar el PAT a revocar
3. Clic en **"..."** → **"Revoke"**
4. Confirmar la revocación

> ✅ **Checkpoint:** PAT creado, copiado y almacenado de forma segura. El PAT aparece en la lista con estado `Active`.

---

## 5. Agent Pools

### 5.1 Conceptos Clave

| Tipo | Descripción | Costo | Uso |
|---|---|---|---|
| **Microsoft-hosted** | Agentes efímeros en la nube de Azure | 1 job gratuito (1800 min/mes), adicionales ~$40 USD/mes | CI — Build, Test, Scan |
| **Self-hosted** | Agentes en servidores propios | Sin costo de licencia (solo infraestructura) | CD — Deploy a IIS On-Prem |

### 5.2 Crear los Agent Pools

Según el estándar, se necesitan **tres pools separados por entorno** para los agentes self-hosted de CD:

1. Ir a **Organization Settings** (engranaje inferior izquierdo → Organization settings)
2. En el menú lateral: **Pipelines → Agent pools**
3. Clic en **"Add pool"** para cada uno:

| Pool Name | Pool Type | Uso |
|---|---|---|
| `Pool-Dev` | Self-hosted | Deploy a entorno Development |
| `Pool-Test` | Self-hosted | Deploy a entorno Testing |
| `Pool-Prod` | Self-hosted | Deploy a entorno Producción |

**Para cada pool:**

4. Seleccionar **Pool type:** `Self-hosted`
5. Ingresar el **Name** según la tabla
6. Marcar: **"Grant access permission to all pipelines"** → `No` (se otorgará explícitamente por pipeline)
7. Clic en **"Create"**

### 5.3 Configurar Permisos de los Pools

Para cada pool creado:

1. Clic en el nombre del pool → pestaña **"Security"**
2. Configurar permisos:

| Grupo/Usuario | Permiso | Valor |
|---|---|---|
| `Project Collection Build Service` | `User` | ✅ Allow |
| `Build Administrators` | `Administrator` | ✅ Allow |
| `Contributors` | `User` | ❌ Deny (en Pool-Prod) |

> 📋 **Para Pool-Prod:** Restringir a solo los pipelines de CD autorizados. No permitir que cualquier pipeline use agentes de producción.

### 5.4 Asociar Pools al Proyecto

1. Ir a **Project Settings → Pipelines → Agent pools**
2. Los pools de la organización aparecerán aquí
3. Verificar que `Pool-Dev`, `Pool-Test` y `Pool-Prod` estén visibles
4. Para cada pool, en la pestaña **"Security"**, configurar qué pipelines tienen acceso

> ✅ **Checkpoint:** Tres pools creados y visibles. Sin agentes registrados aún (estado: 0 agents).

---

## 6. Instalación del Agente Self-Hosted (CD)

### 6.1 Prerrequisitos del Servidor

| Requisito | Detalle |
|---|---|
| **Sistema Operativo** | Windows Server 2016+ |
| **PowerShell** | 5.1+ |
| **RAM** | 2 GB mínimo libre para el agente |
| **Disco** | 10 GB libres mínimo |
| **Software** | .NET Framework 4.6.2+ (para el agente) |
| **NO instalar** | ❌ Visual Studio, ❌ Build Tools, ❌ SDKs de compilación |
| **Conectividad** | HTTPS (443) hacia `dev.azure.com` |

> ⚠️ **El servidor IIS NO compila.** El agente solo descarga artefactos y los despliega.

### 6.2 Crear la Cuenta de Servicio

Ejecutar en PowerShell (como administrador) en el servidor destino:

```powershell
# Crear usuario local de servicio
$password = Read-Host -AsSecureString "Ingrese contraseña para svc_ado_deploy"
New-LocalUser -Name "svc_ado_deploy" `
    -Password $password `
    -Description "Cuenta de servicio para Azure DevOps Agent (deploy IIS via Web Deploy)" `
    -PasswordNeverExpires $true `
    -UserMayNotChangePassword $true

# Solo grupo Users — NO Administrators
# Con Web Deploy + WMSVC, la cuenta NO necesita ser admin local.
# Los deploys se ejecutan vía delegación del Web Management Service.
Add-LocalGroupMember -Group "Users" -Member "svc_ado_deploy"
```

### 6.3 Configurar Permisos de la Cuenta de Servicio

La cuenta de servicio usa **Web Deploy** (msdeploy.exe) vía el **Web Management Service (WMSVC)** para desplegar. Esto elimina la necesidad de admin local. Los permisos necesarios son:

1. **Permisos NTFS con herencia** — sobre las carpetas padre, heredados a todas las apps
2. **Delegación de Web Deploy** — configurada en WMSVC por sitio IIS

```powershell
# Permisos NTFS con herencia (cubre todas las apps presentes y futuras)
icacls "C:\inetpub\wwwroot" /grant "svc_ado_deploy:(OI)(CI)M" /T
icacls "C:\deploy-backups"  /grant "svc_ado_deploy:(OI)(CI)M" /T
icacls "C:\agent"            /grant "svc_ado_deploy:(OI)(CI)F" /T

# La delegación de Web Deploy se configura en WMSVC.
# Ver: DevOps/guia-web-deploy-iis.md — Secciones 4 y 5
```

**Permisos específicos requeridos para `svc_ado_deploy`:**

| Recurso | Permiso | Método | Admin requerido |
|---|---|---|---|
| `C:\inetpub\wwwroot` | Modify (heredado) | `icacls (OI)(CI)M` | Solo al configurar |
| `C:\deploy-backups` | Modify (heredado) | `icacls (OI)(CI)M` | Solo al configurar |
| `C:\agent` | Full Control | `icacls (OI)(CI)F` | Solo al configurar |
| IIS Sites (Stop/Start/Deploy) | Delegado via WMSVC | Web Deploy Delegation Rules | Solo al configurar |
| Grupo `Administrators` | ❌ NO requerido | — | — |

> 📋 **Guía completa:** Para la configuración detallada de Web Deploy, WMSVC, delegación por sitio, y scripts de bootstrap, ver **DevOps/guia-web-deploy-iis.md**.

### 6.4 Descargar e Instalar el Agente

```powershell
# Crear directorio para el agente
$agentDir = "C:\agent"
New-Item -ItemType Directory -Path $agentDir -Force
Set-Location $agentDir

# Descargar el agente (verificar la última versión en la UI de Azure DevOps)
# Ir a Organization Settings → Agent pools → Pool-Dev → New agent → Windows x64
# Copiar el link de descarga

# Alternativa: descarga directa (verificar versión actual)
$agentUrl = "https://vstsagentpackage.azureedge.net/agent/3.248.0/vsts-agent-win-x64-3.248.0.zip"
Invoke-WebRequest -Uri $agentUrl -OutFile "$agentDir\agent.zip"

# Extraer
Expand-Archive -Path "$agentDir\agent.zip" -DestinationPath $agentDir -Force
Remove-Item "$agentDir\agent.zip"
```

> 📋 **Nota:** Siempre verificar la versión más reciente del agente desde la UI de Azure DevOps: **Organization Settings → Agent pools → [Pool] → New agent**.

### 6.5 Configurar el Agente

```powershell
# Ejecutar la configuración
.\config.cmd
```

El asistente interactivo pedirá:

```
Enter server URL > https://dev.azure.com/miempresa-devops
Enter authentication type (press enter for PAT) > [Enter]
Enter personal access token > ************************************
Enter agent pool (press enter for default) > Pool-Dev
Enter agent name (press enter for [hostname]) > agent-dev-01
Enter work folder (press enter for _work) > [Enter]
Enter run agent as service? (Y/N) (press enter for N) > Y
Enter enable SERVICE_SID_TYPE_UNRESTRICTED for agent service (Y/N) (press enter for N) > Y
Enter User account to use for the service (press enter for NT AUTHORITY\NETWORK SERVICE) > .\svc_ado_deploy
Enter Password for the account .\svc_ado_deploy > ****
Enter whether to prevent service starting immediately after configuration is finished? (Y/N) (press enter for N) > N
```

**Parámetros críticos:**

| Parámetro | Valor | Razón |
|---|---|---|
| **Server URL** | `https://dev.azure.com/{org}` | URL de la organización |
| **Auth type** | PAT | Método más simple y controlable |
| **Agent pool** | `Pool-Dev` / `Pool-Test` / `Pool-Prod` | Según el entorno del servidor |
| **Run as service** | Sí | Para ejecución desatendida |
| **Service account** | `.\svc_ado_deploy` | Cuenta dedicada sin admin |

### 6.6 Verificar el Agente

```powershell
# Verificar que el servicio está corriendo
Get-Service -Name "vstsagent.*" | Format-Table Name, Status, StartType

# Verificar en Azure DevOps:
# Organization Settings → Agent pools → Pool-Dev → Agents
# El agente debe aparecer con estado "Online" (círculo verde)
```

### 6.7 Repetir para Cada Entorno

Repetir los pasos 6.4 a 6.6 en cada servidor, registrando:

| Servidor | Pool | Nombre del Agente |
|---|---|---|
| SRV-DEV-01 | `Pool-Dev` | `agent-dev-01` |
| SRV-TEST-01 | `Pool-Test` | `agent-test-01` |
| SRV-PROD-01 | `Pool-Prod` | `agent-prod-01` |

> ✅ **Checkpoint:** Todos los agentes aparecen como `Online` en sus respectivos pools en Azure DevOps.

---

## 7. Service Connections

### 7.1 ¿Cuándo Se Necesitan?

| Escenario | Service Connection Necesaria |
|---|---|
| Deploy a IIS On-Prem vía agente self-hosted | **No** — el agente ya tiene acceso local |
| Integración con Azure Key Vault | **Sí** — Azure Resource Manager service connection |
| Acceso a feed NuGet externo | **Sí** — NuGet service connection |
| Notificaciones a Teams/Slack | **Sí** — Incoming Webhook service connection |

### 7.2 Crear Service Connection para Azure Key Vault (Futuro)

Cuando se migre a Key Vault:

1. Ir a **Project Settings → Service connections**
2. Clic en **"New service connection"**
3. Seleccionar **"Azure Resource Manager"**
4. Seleccionar **"Service principal (automatic)"** o **(manual)**
5. Configurar:

```
Subscription:       [Seleccionar suscripción Azure]
Resource Group:     rg-devops-keyvault
Service connection name: sc-keyvault-prod
Description:        Conexión a Key Vault para secretos de producción
```

6. Marcar: **"Grant access permission to all pipelines"** → `No`
7. Clic en **"Save"**

### 7.3 Principio de Mínimo Privilegio en Service Connections

- Crear **una service connection por entorno** (no compartir entre Dev/Test/Prod)
- Otorgar permisos **solo a los pipelines que los necesitan** (pestaña Security de cada connection)
- Usar **identidad federada** (Workload Identity Federation) cuando sea posible para evitar secretos

> ✅ **Checkpoint:** Service connections requeridas creadas con permisos restringidos.

---

## 8. Variable Groups y Secretos

### 8.1 Estrategia de Variable Groups

Se necesita **un Variable Group por entorno** para cada aplicación:

| Variable Group | Entorno | Contenido |
|---|---|---|
| `vg-miapp-dev` | Development | Connection strings, API keys de dev |
| `vg-miapp-test` | Testing | Connection strings, API keys de test |
| `vg-miapp-prod` | Producción | Connection strings, API keys de prod |

### 8.2 Crear un Variable Group

1. Ir a **Pipelines → Library**
2. Clic en **"+ Variable group"**
3. Configurar:

```
Variable group name:  vg-miapp-dev
Description:          Variables de entorno para MiApp - Development
```

4. Agregar variables:

| Nombre de Variable | Valor | ¿Secreto? |
|---|---|---|
| `ConnectionString` | `Server=srv-dev;Database=MiApp;...` | 🔒 Sí |
| `ApiKey` | `dev-api-key-xxxxx` | 🔒 Sí |
| `AppPoolName` | `MiApp-Dev` | No |
| `WebsiteName` | `MiApp-Dev` | No |
| `DeployPath` | `C:\inetpub\wwwroot\MiApp` | No |
| `SiteUrl` | `https://miapp-dev.empresa.local` | No |

5. Para cada variable secreta: clic en el **icono de candado** 🔒 para marcarla como secreta
6. Clic en **"Save"**

> 🔴 **IMPORTANTE:** Las variables marcadas como secretas se cifran y **no se pueden leer** después de guardarlas. Solo se inyectan en tiempo de ejecución del pipeline.

### 8.3 Vincular Variable Group a un Pipeline

Hay dos formas:

**Forma 1 — En el YAML del pipeline:**

```yaml
variables:
  - group: vg-miapp-dev
```

**Forma 2 — Desde la UI:**

1. Ir a **Pipelines → [Pipeline] → Edit**
2. Clic en **"Variables"** → **"Variable groups"**
3. Clic en **"Link variable group"**
4. Seleccionar el grupo
5. (Opcional) Scope: seleccionar un stage específico

### 8.4 Seguridad de Variable Groups

1. En **Pipelines → Library → [Variable Group]**
2. Pestaña **"Security"**
3. Configurar:

| Rol | Permisos |
|---|---|
| `Project Administrators` | Administrator |
| `Build Administrators` | Administrator |
| `Contributors` | Reader (no pueden editar secretos) |
| Pipeline específico | User (se configura al vincular) |

### 8.5 Evolución a Azure Key Vault

Cuando se migre a Key Vault, se puede vincular directamente:

1. En **Pipelines → Library → "+ Variable group"**
2. Activar toggle: **"Link secrets from an Azure key vault as variables"**
3. Seleccionar la **Service Connection** al suscripción Azure
4. Seleccionar el **Key Vault**
5. Agregar los **secretos** necesarios por nombre
6. Los secretos se sincronizan automáticamente

**Ventajas de Key Vault:**
- Rotación sin redeploy
- Auditoría centralizada
- Versionado de secretos
- Políticas de expiración

> ✅ **Checkpoint:** Variable Groups creados para cada entorno. Variables secretas cifradas. Permisos configurados.

---

## 9. Environments y Approvals

### 9.1 Crear Environments

Los Environments en Azure DevOps permiten:
- Rastrear qué fue desplegado y cuándo
- Configurar aprobaciones y checks
- Ver historial de deploys por entorno

**Crear cada environment:**

1. Ir a **Pipelines → Environments**
2. Clic en **"New environment"**
3. Para cada entorno:

| Environment Name | Resource Type | Descripción |
|---|---|---|
| `Dev` | None | Entorno de desarrollo |
| `Test` | None | Entorno de pruebas |
| `Prod` | None | Producción — requiere aprobación |

### 9.2 Configurar Aprobaciones en Producción

> 🔴 **OBLIGATORIO:** Todo despliegue a Producción requiere aprobación manual.

1. Ir a **Pipelines → Environments → Prod**
2. Clic en **"⋮"** (tres puntos) → **"Approvals and checks"**
3. Clic en **"+ Add check"** → **"Approvals"**
4. Configurar:

```
Approvers:         [Team Lead], [DevOps Lead]
                   (agregar al menos 2 aprobadores)
Instructions:      "Verificar que los tests pasaron, SAST sin críticos,
                    y QA ha dado aprobación funcional."
Minimum approvers: 1
Allow approvers to approve their own runs: No
Timeout:           72 hours (3 días hábiles)
```

5. Clic en **"Create"**

### 9.3 Checks Adicionales Recomendados

| Check | Configuración | Obligatorio |
|---|---|---|
| **Branch control** | Solo permitir deploys desde `main` | ✅ Sí en Prod |
| **Business hours** | Solo permitir deploys Lun-Vie 9:00-17:00 | Recomendado |
| **Required template** | Verificar que el pipeline usa templates corporativos | Recomendado |
| **Exclusive Lock** | Evitar deploys simultáneos al mismo entorno | ✅ Sí |

**Configurar Branch Control en Prod:**

1. En **Environments → Prod → Approvals and checks**
2. **"+ Add check"** → **"Branch control"**
3. Allowed branches: `refs/heads/main`
4. Verify branch protection: ✅

### 9.4 Configuración por Entorno

| Entorno | Aprobación | Branch Control | Exclusive Lock |
|---|---|---|---|
| Dev | ❌ No | ❌ No | ❌ No |
| Test | ⚡ Opcional | Recomendado (`main`, `develop`) | ✅ Sí |
| Prod | ✅ Obligatoria | ✅ Solo `main` | ✅ Sí |

> ✅ **Checkpoint:** Tres environments creados. Producción con aprobación manual configurada. Branch control activo en Prod.

---

## 10. Repositorios y Branching

### 10.1 Estrategia de Branching

Se recomienda **Git Flow simplificado**:

```
main             ← Código en producción (protegido)
  └── develop    ← Integración continua
       ├── feature/nueva-funcionalidad
       ├── feature/fix-bug-123
       └── feature/mejora-rendimiento
```

### 10.2 Crear el Repositorio

1. Ir a **Repos → Files**
2. Si es un proyecto nuevo, el repo `default` ya existe
3. Para repos adicionales: **Repos → "+" → New repository**

### 10.3 Configurar Branch Policies en `main`

1. Ir a **Repos → Branches**
2. En la rama `main`, clic en **"⋮"** → **"Branch policies"**
3. Configurar:

**Require a minimum number of reviewers:**
```
✅ Enabled
Minimum number of reviewers: 2
Allow requestors to approve their own changes: No
Reset all approval votes when source branch changes: Yes
```

**Check for linked work items:**
```
✅ Required
```

**Check for comment resolution:**
```
✅ Required
```

**Build validation:**
```
✅ Add build policy
Build pipeline:    [Seleccionar pipeline CI]
Trigger:           Automatic
Policy requirement: Required
Build expiration:  12 hours
Display name:      "CI Build Validation"
```

### 10.4 Configurar Branch Policies en `develop`

Similar a `main` pero con menos restricciones:

```
Minimum reviewers: 1
Build validation:  Required (pipeline CI)
Comment resolution: Optional
```

### 10.5 Estructura del Repositorio Recomendada

```
/
├── src/
│   └── MiAplicacion/
│       ├── MiAplicacion.sln
│       ├── MiAplicacion.Web/
│       │   ├── MiAplicacion.Web.csproj
│       │   ├── web.config
│       │   └── ...
│       └── MiAplicacion.Tests/
│           ├── MiAplicacion.Tests.csproj
│           └── ...
├── pipelines/
│   ├── pipeline-dotnet-webapp.yml
│   ├── pipeline-netfx-webapp.yml
│   └── pipeline-security-scan.yml
├── templates/
│   ├── ci-build.yml
│   ├── ci-build-netfx.yml
│   ├── cd-deploy-iis.yml
│   ├── cd-deploy-iis-netfx.yml
│   ├── stages-ci.yml
│   └── stages-cd.yml
├── NuGet.config          (si hay feeds privados)
├── .gitignore
└── README.md
```

> ✅ **Checkpoint:** Repositorio creado con branch policies. Build validation automática en PRs a `main` y `develop`.

---

## 11. Pipelines — Creación desde YAML

### 11.1 Crear un Pipeline

1. Ir a **Pipelines → Pipelines**
2. Clic en **"New pipeline"**
3. Seleccionar **"Azure Repos Git"**
4. Seleccionar el repositorio
5. Seleccionar **"Existing Azure Pipelines YAML file"**
6. Configurar:

```
Branch: main
Path:   /pipelines/pipeline-dotnet-webapp.yml
```

7. Clic en **"Continue"**
8. Revisar el YAML → Clic en **"Run"** (primera ejecución) o **"Save"** (para guardar sin ejecutar)

### 11.2 Renombrar el Pipeline

1. Después de crear, ir a **Pipelines → [pipeline recién creado]**
2. Clic en **"⋮"** → **"Rename/move"**
3. Nombre: `CI-CD-MiApp-DotNet` (convención descriptiva)
4. Folder: `\MiAplicacion` (organizar por aplicación)

### 11.3 Permisos del Pipeline sobre Recursos

Al primera ejecución, el pipeline pedirá permisos sobre:

- **Agent Pools** — Clic en "Permit" para los pools requeridos
- **Variable Groups** — Clic en "Permit" para los grupos linkeados
- **Environments** — Clic en "Permit" para los environments referenciados

> 📋 **Nota Security:** En vez de "Permit all", es preferible ir a cada recurso y autorizar solo el pipeline específico.

### 11.4 Triggers

Los pipelines YAML usan triggers definidos en el propio archivo:

```yaml
# Trigger automático en push a estas ramas
trigger:
  branches:
    include:
      - main
      - develop
  paths:
    include:
      - src/**
    exclude:
      - '**/*.md'

# Sin trigger de PR — se usa build validation en branch policies
pr: none
```

### 11.5 Convención de Nombres de Artefactos

```
Formato: {AppName}-{BuildNumber}-{BranchName}
Ejemplo: MiApp-20260304.1-main
```

Esto se configura en el pipeline:

```yaml
name: '$(Date:yyyyMMdd).$(Rev:r)'
```

Y en el step de publicar artefacto:

```yaml
- publish: $(Build.ArtifactStagingDirectory)
  artifact: 'MiApp-$(Build.BuildNumber)-$(Build.SourceBranchName)'
```

> ✅ **Checkpoint:** Pipeline creado desde YAML, renombrado y con permisos sobre pools, variable groups y environments configurados.

---

## 12. Proyectos .NET Core / .NET 5+

### 12.1 Requisitos del Pipeline CI

| Paso | Herramienta | Agente |
|---|---|---|
| Restore | `dotnet restore` | Microsoft-hosted |
| Build | `dotnet build` | Microsoft-hosted |
| Test | `dotnet test` | Microsoft-hosted |
| SAST | Microsoft Security DevOps | Microsoft-hosted |
| Dependency Scan | NuGet vulnerability audit | Microsoft-hosted |
| Publish Artifact | `dotnet publish` + `PublishPipelineArtifact` | Microsoft-hosted |

### 12.2 Archivos YAML Involucrados

```
templates/ci-build.yml          → Template reutilizable para CI
templates/cd-deploy-iis.yml     → Template reutilizable para CD
templates/stages-ci.yml         → Stage wrapper para CI
templates/stages-cd.yml         → Stage wrapper para CD
pipelines/pipeline-dotnet-webapp.yml → Pipeline principal
```

### 12.3 Pipeline Principal

El pipeline `pipeline-dotnet-webapp.yml` orquesta todo:

1. **Stage CI**: Compila en Microsoft-hosted → publica artefacto
2. **Stage CD-Dev**: Despliega en IIS Dev → sin aprobación
3. **Stage CD-Test**: Despliega en IIS Test → depende de CD-Dev exitoso
4. **Stage CD-Prod**: Despliega en IIS Prod → aprobación manual obligatoria

Ver el archivo `pipelines/pipeline-dotnet-webapp.yml` en el repositorio para el detalle completo.

### 12.4 Variables Necesarias por Entorno

```yaml
# vg-dotnet-dev / vg-dotnet-test / vg-dotnet-prod
ConnectionString: "Server=...;Database=...;..."    # 🔒 Secreto
AppPoolName: "MiApp-[Env]"
WebsiteName: "MiApp-[Env]"
DeployPath: "C:\inetpub\wwwroot\MiApp"
SiteUrl: "https://miapp-[env].empresa.local"
```

---

## 13. Proyectos Legacy .NET Framework 4.x

### 13.1 Diferencias Clave vs .NET Core

| Aspecto | .NET Core / .NET 5+ | .NET Framework 4.x |
|---|---|---|
| **Build tool** | `dotnet build` | `MSBuild` (via `VSBuild@1`) |
| **Restore** | `dotnet restore` | `NuGet.exe` (via `NuGetCommand@2`) |
| **Test runner** | `dotnet test` | `VSTest@2` |
| **Publish** | `dotnet publish` | MSBuild con `/p:DeployOnBuild=true` |
| **Output** | Carpeta auto-contenida | Web Deploy Package (`.zip`) |
| **Agente CI** | `windows-latest` o `ubuntu-latest` | `windows-latest` **obligatorio** |

### 13.2 Requisitos del Pipeline CI

| Paso | Herramienta | Task de Azure DevOps |
|---|---|---|
| Instalar NuGet | NuGet.exe | `NuGetToolInstaller@1` |
| Restore | NuGet restore | `NuGetCommand@2` |
| Build | MSBuild | `VSBuild@1` |
| Test | VSTest | `VSTest@2` |
| SAST | Microsoft Security DevOps | `MicrosoftSecurityDevOps@1` |
| Publish Artifact | Copy + Publish | `PublishPipelineArtifact@1` |

### 13.3 Configuración de MSBuild Args

```yaml
msbuildArgs: >-
  /p:DeployOnBuild=true
  /p:WebPublishMethod=Package
  /p:PackageAsSingleFile=true
  /p:SkipInvalidConfigurations=true
  /p:PackageLocation="$(Build.ArtifactStagingDirectory)"
```

Esto genera un **Web Deploy Package** (`.zip`) con todo lo necesario para desplegar.

### 13.4 Deploy sin Web Deploy (xcopy)

Si el servidor destino **no tiene Web Deploy instalado**, se usa xcopy/PowerShell:

```powershell
# En el agente self-hosted (template cd-deploy-iis-netfx.yml)
# 1. Detener el sitio IIS
Stop-WebSite -Name $websiteName

# 2. Backup del contenido actual
Copy-Item -Path $deployPath -Destination "$backupPath\backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')" -Recurse

# 3. Limpiar y copiar nuevos archivos
Remove-Item "$deployPath\*" -Recurse -Force
Copy-Item -Path "$artifactPath\*" -Destination $deployPath -Recurse

# 4. Aplicar transformaciones de web.config
# (Inyectar connection strings y configuración del entorno)

# 5. Reiniciar el sitio
Start-WebSite -Name $websiteName
```

### 13.5 Deploy con Web Deploy

Si Web Deploy está disponible:

```powershell
# Usar msdeploy.exe
& "C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe" `
    -verb:sync `
    -source:package="$artifactPath\MiApp.zip" `
    -dest:auto,computerName="localhost" `
    -setParam:name="IIS Web Application Name",value="$websiteName" `
    -allowUntrusted
```

### 13.6 Archivos YAML Involucrados

```
templates/ci-build-netfx.yml           → Template CI para .NET Framework
templates/cd-deploy-iis-netfx.yml      → Template CD para .NET Framework
pipelines/pipeline-netfx-webapp.yml    → Pipeline principal legacy
```

### 13.7 NuGet Feeds Privados

Si la aplicación usa paquetes de un feed NuGet interno, crear `NuGet.config` en la raíz del repo:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="empresa-feed" value="https://pkgs.dev.azure.com/miempresa/_packaging/MiFeed/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

Y en el template CI:

```yaml
- task: NuGetCommand@2
  inputs:
    command: 'restore'
    restoreSolution: '$(solution)'
    feedsToUse: 'config'
    nugetConfigPath: 'NuGet.config'
```

> ✅ **Checkpoint:** Comprender diferencias entre .NET Core y .NET Framework para elegir el pipeline correcto.

---

## 14. Gates de Seguridad (SAST + Dependency Scan)

### 14.1 Instalar Microsoft Security DevOps

1. Ir a [Visual Studio Marketplace](https://marketplace.visualstudio.com/)
2. Buscar **"Microsoft Security DevOps"**
3. Clic en **"Get it free"**
4. Seleccionar la organización de Azure DevOps
5. Clic en **"Install"**

> 📋 Esta extensión es **gratuita** e incluye herramientas como:
> - **Credential Scanner** — detecta secretos en código
> - **BinSkim** — análisis de binarios
> - **Template Analyzer** — análisis de templates ARM/Bicep
> - **Terrascan** — si se usa Terraform

### 14.2 Configurar SAST en el Pipeline CI

El task se incluye en los templates CI:

```yaml
- task: MicrosoftSecurityDevOps@1
  displayName: 'SAST - Microsoft Security DevOps'
  inputs:
    categories: 'code'
  continueOnError: false  # Bloquea el pipeline si hay hallazgos críticos
```

### 14.3 Escaneo de Dependencias NuGet

Para .NET Core:

```yaml
- task: DotNetCoreCLI@2
  displayName: 'Dependency Scan - NuGet Audit'
  inputs:
    command: 'custom'
    custom: 'list'
    arguments: 'package --vulnerable --include-transitive'
    projects: '$(projectPath)'
```

Para .NET Framework, se evalúan las dependencias durante el SAST.

### 14.4 Política de Bloqueo

| Entorno | Comportamiento ante hallazgos |
|---|---|
| **Dev** | Warning — no bloquea |
| **Test** | Warning — no bloquea |
| **Prod** | Blocking — no se despliega con vulnerabilidades altas/críticas |

Implementación en el pipeline:

```yaml
# En el stage de SAST
- task: MicrosoftSecurityDevOps@1
  displayName: 'SAST Scan'
  inputs:
    categories: 'code'
  # En Dev/Test: continueOnError: true
  # En Prod: continueOnError: false
  continueOnError: ${{ eq(parameters.environment, 'Prod') }}
```

### 14.5 Interpretar Resultados

Los resultados se publican como **SARIF** en la pestaña **"Scans"** del pipeline run:

1. Ir al pipeline run completado
2. Pestaña **"Scans"** o **"Extensions"**
3. Ver detalle de cada hallazgo: severidad, ubicación, recomendación

> ✅ **Checkpoint:** Extensión MSDO instalada. SAST integrado en pipeline CI. Política de bloqueo definida.

---

## 15. IIS — Preparación del Servidor Destino

### 15.1 Habilitar IIS, ASP.NET y Web Deploy

> 📋 **Guía completa:** Para la instalación detallada paso a paso, incluyendo Web Deploy, WMSVC y scripts de bootstrap, ver **DevOps/guia-web-deploy-iis.md**.

```powershell
# En el servidor destino (PowerShell como administrador)

# Instalar IIS + features necesarias (incluyendo Management Service para Web Deploy)
Install-WindowsFeature -Name @(
    'Web-Server',
    'Web-Asp-Net45',
    'Web-Net-Ext45',
    'Web-ISAPI-Ext',
    'Web-ISAPI-Filter',
    'Web-Mgmt-Console',
    'Web-Mgmt-Service',         # ← Necesario para Web Deploy
    'Web-Scripting-Tools'
) -IncludeManagementTools

# Para .NET Core: instalar el ASP.NET Core Hosting Bundle
# Descargar desde: https://dotnet.microsoft.com/download/dotnet
# Ejecutar: dotnet-hosting-X.X.X-win.exe

# Instalar Web Deploy 3.6 (con TODAS las features)
# Descargar desde: https://www.iis.net/downloads/microsoft/web-deploy
# Instalar con: msiexec /i WebDeploy_amd64.msi ADDLOCAL=ALL /quiet

# Verificar instalación
Get-WindowsFeature Web-* | Where-Object Installed | Format-Table Name, InstallState
```

### 15.2 Verificar lo que NO Debe Estar Instalado

```powershell
# Verificar que NO hay Visual Studio instalado
$vs = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\VisualStudio\*" -ErrorAction SilentlyContinue
if ($vs) { Write-Warning "⚠️ Visual Studio detectado — DEBE ser removido del servidor" }

# Verificar que NO hay Build Tools
$bt = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\VisualStudio\*\BuildTools" -ErrorAction SilentlyContinue
if ($bt) { Write-Warning "⚠️ Build Tools detectados — DEBEN ser removidos" }

# Si no se detecta nada: OK
Write-Host "✅ Servidor limpio — solo IIS y runtime"
```

### 15.3 Crear Estructura de Carpetas

```powershell
# Carpeta de deploy
$appPath = "C:\inetpub\wwwroot\MiAplicacion"
New-Item -ItemType Directory -Path $appPath -Force

# Carpeta de backups
$backupPath = "C:\deploy-backups\MiAplicacion"
New-Item -ItemType Directory -Path $backupPath -Force

# Permisos para la cuenta de servicio
$acl = Get-Acl $appPath
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    "svc_ado_deploy", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow"
)
$acl.SetAccessRule($rule)
Set-Acl $appPath $acl

$acl = Get-Acl $backupPath
$acl.SetAccessRule($rule)
Set-Acl $backupPath $acl
```

### 15.4 Crear Sitio IIS

```powershell
Import-Module WebAdministration

# Crear Application Pool
New-WebAppPool -Name "MiApp-Pool"
Set-ItemProperty "IIS:\AppPools\MiApp-Pool" -Name "managedRuntimeVersion" -Value "v4.0"
# Para .NET Core: managedRuntimeVersion = "" (No Managed Code)

# Crear Website
New-Website -Name "MiAplicacion" `
    -PhysicalPath $appPath `
    -ApplicationPool "MiApp-Pool" `
    -Port 443 `
    -Ssl `
    -HostHeader "miapp.empresa.local"

# Verificar
Get-Website | Format-Table Name, State, PhysicalPath
```

> ✅ **Checkpoint:** IIS habilitado con ASP.NET. Sin Visual Studio ni Build Tools. Sitio creado con app pool configurado.

---

## 16. Rollback y Backup

### 16.1 Estrategia de Backup Pre-Deploy

Antes de cada despliegue, el pipeline CD realiza automáticamente:

```
C:\deploy-backups\MiAplicacion\
├── backup-20260304-143022\    ← Backup del deploy de hoy
│   ├── bin\
│   ├── web.config
│   └── ...
├── backup-20260303-091500\    ← Backup de ayer
└── backup-20260301-160000\    ← Backup de hace 3 días
```

### 16.2 Procedimiento de Rollback Manual

Si un deploy falla o causa problemas:

```powershell
# 1. Identificar el último backup válido
$lastBackup = Get-ChildItem "C:\deploy-backups\MiAplicacion" |
    Sort-Object CreationTime -Descending |
    Select-Object -First 1

Write-Host "Restaurando desde: $($lastBackup.FullName)"

# 2. Detener el sitio
Import-Module WebAdministration
Stop-WebSite -Name "MiAplicacion"

# 3. Restaurar
$deployPath = "C:\inetpub\wwwroot\MiAplicacion"
Remove-Item "$deployPath\*" -Recurse -Force
Copy-Item -Path "$($lastBackup.FullName)\*" -Destination $deployPath -Recurse

# 4. Reiniciar
Start-WebSite -Name "MiAplicacion"

# 5. Verificar
$response = Invoke-WebRequest -Uri "https://miapp.empresa.local/health" -UseBasicParsing
Write-Host "Status: $($response.StatusCode)"
```

### 16.3 Política de Retención de Backups

```powershell
# Mantener solo los últimos 5 backups (ejecutar periódicamente o al inicio del deploy)
$backupDir = "C:\deploy-backups\MiAplicacion"
$backups = Get-ChildItem $backupDir | Sort-Object CreationTime -Descending
$backups | Select-Object -Skip 5 | Remove-Item -Recurse -Force
```

### 16.4 Rollback Automático en Pipeline

El template CD incluye un health check post-deploy. Si falla:

```yaml
- powershell: |
    try {
        $response = Invoke-WebRequest -Uri "$(SiteUrl)/health" -UseBasicParsing -TimeoutSec 30
        if ($response.StatusCode -ne 200) { throw "Health check failed: $($response.StatusCode)" }
        Write-Host "✅ Health check passed"
    } catch {
        Write-Host "❌ Health check failed — iniciando rollback"
        # Restaurar backup
        $lastBackup = Get-ChildItem "$(BackupPath)" | Sort-Object CreationTime -Descending | Select-Object -First 1
        Stop-WebSite -Name "$(WebsiteName)"
        Remove-Item "$(DeployPath)\*" -Recurse -Force
        Copy-Item "$($lastBackup.FullName)\*" -Destination "$(DeployPath)" -Recurse
        Start-WebSite -Name "$(WebsiteName)"
        throw "Deploy failed — rolled back to $($lastBackup.Name)"
    }
  displayName: 'Health Check + Auto-Rollback'
```

> ✅ **Checkpoint:** Backup automático pre-deploy. Rollback manual documentado. Rollback automático si health check falla.

---

## 17. Auditoría y Observabilidad

### 17.1 Retención de Logs

Configurar retención en **Project Settings → Pipelines → Settings → Retention**:

| Tipo | Retención Recomendada |
|---|---|
| Pipeline runs (builds) | 90 días |
| Pipeline runs (releases) | 180 días |
| Artefactos | 30 días (o según política de la organización) |

### 17.2 Historial de Aprobaciones

Cada aprobación queda registrada:

1. Ir a **Pipelines → Environments → Prod**
2. Ver **"Deployment history"**
3. Cada entry muestra: quién aprobó, cuándo, qué pipeline, qué artefacto

### 17.3 Health Check Post-Deploy

Todos los templates CD incluyen un health check:

```powershell
# Health check básico
$response = Invoke-WebRequest -Uri "$(SiteUrl)/health" -UseBasicParsing -TimeoutSec 30

if ($response.StatusCode -eq 200) {
    Write-Host "✅ Aplicación respondiendo correctamente"
} else {
    throw "❌ Health check failed: Status $($response.StatusCode)"
}
```

### 17.4 Notificaciones

Configurar notificaciones en **Project Settings → Notifications**:

| Evento | Destinatario |
|---|---|
| Build failed | Team |
| Deploy to Prod completed | Stakeholders |
| Approval pending | Aprobadores designados |

---

## 18. Rotación de PATs y Secretos

### 18.1 Checklist de Rotación (Ejecutar cada 90 días)

- [ ] **PATs de agentes:** Crear nuevo PAT → reconfigurar agentes → revocar PAT anterior
- [ ] **Variable Groups:** Revisar y rotar secretos (connection strings, API keys)
- [ ] **Service Connections:** Verificar expiración de service principals
- [ ] **Cuentas de servicio:** Cambiar contraseña de `svc_ado_deploy` (si se usan contraseñas)

### 18.2 Rotación de PAT de Agente — Sin Downtime

```powershell
# En el servidor del agente:

# 1. Crear nuevo PAT en Azure DevOps (UI)
# 2. Detener el servicio del agente
Stop-Service "vstsagent.*"

# 3. Reconfigurar con el nuevo PAT
Set-Location C:\agent
.\config.cmd remove --auth pat --token OLD_PAT_TOKEN
.\config.cmd --unattended `
    --url https://dev.azure.com/miempresa-devops `
    --auth pat --token NEW_PAT_TOKEN `
    --pool "Pool-Dev" `
    --agent "agent-dev-01" `
    --runAsService `
    --windowsLogonAccount ".\svc_ado_deploy" `
    --windowsLogonPassword "PASSWORD"

# 4. Verificar
Get-Service "vstsagent.*" | Format-Table Name, Status
# En Azure DevOps: verificar que el agente está Online

# 5. Revocar el PAT anterior en la UI de Azure DevOps
```

### 18.3 Registro de Rotaciones

Mantener un registro (puede ser en un Wiki de Azure DevOps o tabla):

| Fecha | Recurso | Acción | Responsable | Siguiente Rotación |
|---|---|---|---|---|
| 2026-03-04 | PAT-Agent-Dev | Rotado | admin@empresa.com | 2026-06-02 |
| 2026-03-04 | vg-miapp-prod | ConnectionString rotado | dba@empresa.com | 2026-06-02 |

---

## 19. Troubleshooting Común

### 19.1 Tabla de Errores Frecuentes

| Error | Causa Probable | Solución |
|---|---|---|
| `No agent found in pool 'Pool-Dev'` | Agente offline o no registrado | Verificar servicio del agente: `Get-Service vstsagent.*`. Reiniciar si es necesario. Verificar conectividad de red. |
| `Access denied` al desplegar en IIS | Permisos insuficientes de `svc_ado_deploy` | Verificar ACL de la carpeta: `Get-Acl C:\inetpub\wwwroot\MiApp`. Añadir permiso `Modify`. |
| `SAST scan failed with exit code 1` | Hallazgos críticos de seguridad | Revisar pestaña "Scans" del pipeline. Corregir hallazgos o, en Dev/Test, usar `continueOnError: true`. |
| `Artifact not found` | Nombre de artefacto incorrecto entre CI y CD | Verificar que `artifactName` sea idéntico en `PublishPipelineArtifact` y `DownloadPipelineArtifact`. |
| `The pipeline is not valid` | Error de sintaxis YAML | Validar YAML en la UI: Pipelines → Edit → validar con el botón "Validate". |
| `Approval timeout` | Nadie aprobó en el tiempo configurado | Contactar aprobadores. Extender timeout si es necesario. |
| `web.config transformation failed` | Variables no definidas o Variable Group no vinculado | Verificar que el Variable Group esté linkeado al stage correcto. Verificar nombres de variables. |
| `Health check failed` | Aplicación no responde post-deploy | Verificar logs de IIS: `C:\inetpub\logs\LogFiles`. Verificar Application Event Log. Considerar rollback. |
| `NuGet restore failed` | Feed privado no configurado o sin autenticación | Verificar `NuGet.config` y que el feed esté accesible. Si es feed de Azure DevOps, el agente Microsoft-hosted lo autentica automáticamente. |
| `Could not install service` al configurar agente | Permisos insuficientes para instalar servicio Windows | Ejecutar `config.cmd` como **administrador**. La cuenta de servicio necesita "Log on as a service". |

### 19.2 Diagnóstico del Agente

```powershell
# Ver logs del agente
Get-Content "C:\agent\_diag\Agent_*.log" -Tail 50

# Ver logs del worker
Get-Content "C:\agent\_diag\Worker_*.log" -Tail 50

# Verificar la versión del agente
& "C:\agent\bin\Agent.Listener.exe" --version

# Verificar estado de conexión
& "C:\agent\bin\Agent.Listener.exe" --diagnostics
```

### 19.3 Verificar Permisos IIS Rápidamente

```powershell
# Script de verificación rápida
$account = "svc_ado_deploy"
$paths = @(
    "C:\inetpub\wwwroot\MiAplicacion",
    "C:\deploy-backups\MiAplicacion",
    "C:\agent"
)

foreach ($path in $paths) {
    $acl = Get-Acl $path
    $hasAccess = $acl.Access | Where-Object { $_.IdentityReference -like "*$account*" }
    if ($hasAccess) {
        Write-Host "✅ $path — $($hasAccess.FileSystemRights)" -ForegroundColor Green
    } else {
        Write-Host "❌ $path — SIN ACCESO" -ForegroundColor Red
    }
}
```

---

## 20. Roadmap de Seguridad

### Fase 1 — Baseline (Implementación Actual)

- [x] Microsoft Security DevOps (SAST) en cada build
- [x] Dependency Scan básico (NuGet audit)
- [x] Aprobaciones manuales en Producción
- [x] Backup manual pre-deploy
- [x] Variable Groups cifrados
- [x] Agentes con cuentas de servicio dedicadas

### Fase 2 — Integración Avanzada

- [ ] Migración de secretos a Azure Key Vault
- [ ] Integración con Snyk u otra herramienta de SCA como stage independiente
- [ ] Modo warning en Dev/Test, blocking en Producción
- [ ] Rotación automática de secretos vía Key Vault
- [ ] Service Connection con Workload Identity Federation (sin secretos)

### Fase 3 — Madurez

- [ ] DAST (Dynamic Application Security Testing) post-deploy
- [ ] Container scanning (si se migra a containers)
- [ ] Policy as Code para validación de pipelines
- [ ] Integración con SIEM corporativo
- [ ] Alertas automáticas por anomalía en deploys

---

## Apéndice A — Estructura de Archivos YAML

```
templates/
├── ci-build.yml              # CI para .NET Core / .NET 5+
│   Parámetros: buildConfiguration, dotnetVersion, projectPath, artifactName
│   Pasos: restore → build → test → SAST → dependency scan → publish
│
├── ci-build-netfx.yml        # CI para .NET Framework 4.x
│   Parámetros: solution, buildPlatform, buildConfiguration, nugetVersion, artifactName
│   Pasos: NuGet install → restore → MSBuild → VSTest → SAST → publish
│
├── cd-deploy-iis.yml         # CD para .NET Core desplegado en IIS
│   Parámetros: environment, pool, websiteName, appPoolName, deployPath, variableGroup,
│               artifactName, siteUrl, httpPort, backupPath, useWebDeploy
│   Modos: Web Deploy (msdeploy.exe via WMSVC, sin admin) | xcopy (con admin)
│   Pasos: download → backup → verify site → deploy (msdeploy o xcopy) → config → health check
│
├── cd-deploy-iis-netfx.yml   # CD para .NET Framework en IIS
│   Parámetros: environment, pool, websiteName, deployPath, variableGroup, artifactName,
│               siteUrl, backupPath, useWebDeploy
│   Pasos: download → backup → stop IIS → deploy (xcopy o WebDeploy) → config → start IIS → health check
│
├── stages-ci.yml             # Stage wrapper — encapsula el template CI con pool y condiciones
│   Parámetros: hereda los del template CI seleccionado
│
└── stages-cd.yml             # Stage wrapper — encapsula el template CD con environment, approval, dependsOn
    Parámetros: hereda los del template CD seleccionado + dependsOn, condition

pipelines/
├── pipeline-dotnet-webapp.yml     # Pipeline completo .NET Core (CI + CD Dev/Test/Prod)
├── pipeline-netfx-webapp.yml      # Pipeline completo .NET Framework (CI + CD Dev/Test/Prod)
└── pipeline-security-scan.yml     # Pipeline standalone de SAST (on-demand o scheduled)
```

---

## Apéndice B — Checklist de Verificación

### B.1 Antes de la Primera Ejecución

- [ ] Organización y proyecto creados
- [ ] PATs generados y almacenados de forma segura
- [ ] Agent Pools creados (Pool-Dev, Pool-Test, Pool-Prod)
- [ ] Agentes self-hosted instalados y online en cada pool
- [ ] Variable Groups creados y secretos cifrados por entorno
- [ ] Environments creados con aprobaciones en Prod
- [ ] Extensión Microsoft Security DevOps instalada
- [ ] IIS configurado en servidores destino
- [ ] Carpetas de deploy y backup creadas con permisos
- [ ] Branch policies configuradas en `main`
- [ ] Archivos YAML comiteados en el repositorio
- [ ] Pipeline creado apuntando al YAML correcto

### B.2 Después de la Primera Ejecución Exitosa

- [ ] CI compiló y publicó artefacto
- [ ] SAST ejecutado sin errores bloqueantes
- [ ] Artefacto desplegado en Dev exitosamente
- [ ] Health check en Dev pasó
- [ ] Deploy a Test ejecutado (si se aprobó)
- [ ] Deploy a Prod esperando aprobación (no auto-deploy)
- [ ] Logs de pipeline accesibles y completos
- [ ] Historial visible en Environments

### B.3 Verificación Periódica (Mensual)

- [ ] Agentes online y actualizados
- [ ] PATs no expirados (o rotados)
- [ ] Variable Groups revisados
- [ ] Backups de deploy existentes y no corrompidos
- [ ] Retención de pipeline configurada
- [ ] Permisos de pools y environments sin cambios no autorizados

---

**Fin del Manual**

*Versión 1.0 — Marzo 2026*  
*Basado en: Estándar Corporativo de Pipelines (especificaciones.md)*
