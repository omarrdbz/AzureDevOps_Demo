# Guía de Configuración: Web Deploy + IIS para Azure DevOps

**Versión:** 1.0  
**Fecha:** Marzo 2026  
**Complemento de:** Manual de Configuración de Azure DevOps (manual-azure-devops.md)  
**Enfoque:** Despliegue automatizado sin privilegios de administrador local

---

## Tabla de Contenido

1. [Introducción y Arquitectura](#1-introducción-y-arquitectura)
2. [Instalación del Servidor](#2-instalación-del-servidor)
3. [Cuenta de Servicio y Permisos (Mínimo Privilegio)](#3-cuenta-de-servicio-y-permisos-mínimo-privilegio)
4. [Configuración de Web Management Service (WMSVC)](#4-configuración-de-web-management-service-wmsvc)
5. [Delegación de Web Deploy por Sitio](#5-delegación-de-web-deploy-por-sitio)
6. [Primer Deploy Automático (Sitio No Existe)](#6-primer-deploy-automático-sitio-no-existe)
7. [Servidor con Múltiples Aplicaciones](#7-servidor-con-múltiples-aplicaciones)
8. [Script Bootstrap Completo](#8-script-bootstrap-completo)
9. [Integración con Azure DevOps Pipelines](#9-integración-con-azure-devops-pipelines)
10. [Troubleshooting](#10-troubleshooting)
11. [Checklist de Verificación](#11-checklist-de-verificación)

---

## 1. Introducción y Arquitectura

### 1.1 ¿Qué es Web Deploy?

Web Deploy (también conocido como **MSDeploy**) es una herramienta de Microsoft que permite sincronizar contenido y configuración de aplicaciones web hacia servidores IIS. A diferencia de la copia directa de archivos (xcopy), Web Deploy ofrece:

- **Sync diferencial** — solo transfiere archivos que cambiaron
- **Delegación sin admin** — un usuario sin privilegios de administrador local puede desplegar sobre su sitio específico
- **App Offline automático** — detiene las peticiones al sitio durante el deploy sin necesidad de `Stop-WebSite`
- **Transaccional** — si el deploy falla a mitad, revierte los cambios
- **Parametrización** — inyecta configuración (connection strings, app settings) en tiempo de deploy

### 1.2 ¿Por qué Web Deploy en lugar de xcopy + appcmd?

| Aspecto | xcopy + appcmd | Web Deploy (msdeploy) |
|---------|---------------|----------------------|
| **Permisos del agente** | ❌ Admin local obligatorio | ✅ Sin admin — delegación por sitio |
| **Primer deploy** | ❌ Requiere crear sitio manualmente o con admin | ✅ Automático vía script bootstrap (una sola vez) |
| **Múltiples apps** | ⚠️ Admin puede tocar todas las apps | ✅ Cada cuenta solo accede a sus sitios delegados |
| **App Offline** | Manual (Stop/Start WebSite) | Automático (`-enableRule:AppOffline`) |
| **Sync diferencial** | ❌ Copia todo siempre | ✅ Solo archivos cambiados |
| **Auditoría** | Logs de pipeline solamente | Logs de pipeline + logs de WMSVC |

### 1.3 Arquitectura

```
┌──────────────────────────────────────────────────────────────┐
│                    SERVIDOR IIS On-Prem                        │
│                                                                │
│  ┌─────────────────────────────────────────────────────────┐  │
│  │  Azure DevOps Agent (svc_ado_deploy — sin admin local)  │  │
│  │                                                          │  │
│  │  Pipeline CD ejecuta:                                    │  │
│  │    msdeploy.exe -verb:sync                               │  │
│  │      -source:contentPath="artefacto"                     │  │
│  │      -dest:auto,computerName="localhost"                  │  │
│  │      -enableRule:AppOffline                               │  │
│  └────────────────────────┬────────────────────────────────┘  │
│                           │                                    │
│                           ▼                                    │
│  ┌─────────────────────────────────────────────────────────┐  │
│  │  Web Management Service (WMSVC) — Puerto 8172           │  │
│  │  Corre como: Local Service (privilegiado)                │  │
│  │  Delega operaciones hacia IIS bajo su propia identidad   │  │
│  └────────────────────────┬────────────────────────────────┘  │
│                           │                                    │
│                           ▼                                    │
│  ┌───────────┐  ┌───────────┐  ┌───────────┐                  │
│  │ WeatherApp │  │ HRPortal  │  │ FinanceApp│  ...             │
│  │ (delegado  │  │ (delegado │  │ (delegado │                  │
│  │  a cuenta) │  │  a cuenta)│  │  a cuenta)│                  │
│  └───────────┘  └───────────┘  └───────────┘                  │
└──────────────────────────────────────────────────────────────┘
```

**Flujo clave:** La cuenta `svc_ado_deploy` **no tiene admin local**. El servicio WMSVC (que sí tiene privilegios) actúa como intermediario y ejecuta las operaciones de IIS en nombre de la cuenta, pero **solo sobre los sitios que le fueron delegados**.

---

## 2. Instalación del Servidor

### 2.1 Requisitos Previos

| Requisito | Detalle |
|-----------|---------|
| **Sistema Operativo** | Windows Server 2016+ |
| **IIS** | Debe estar habilitado con módulos de gestión |
| **.NET Runtime** | ASP.NET Core Hosting Bundle (para .NET Core) o ASP.NET 4.x |
| **Web Deploy** | Versión 3.6 o superior |
| **Conectividad** | Puerto 8172 (WMSVC) accesible desde localhost |

### 2.2 Script de Instalación Completa

Ejecutar como **Administrador** en el servidor destino:

```powershell
# =============================================================================
# Script: Instalar IIS + Web Deploy + WMSVC
# Ejecutar UNA SOLA VEZ como Administrador en cada servidor de deploy
# =============================================================================

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  INSTALACIÓN DE IIS + WEB DEPLOY"
Write-Host "=========================================" -ForegroundColor Cyan

# ── 1. Features de IIS ──────────────────────────────────────────────────────
Write-Host "`n[1/5] Instalando features de IIS..." -ForegroundColor Yellow

Install-WindowsFeature -Name @(
    'Web-Server',                  # IIS Web Server
    'Web-Asp-Net45',               # ASP.NET 4.x
    'Web-Net-Ext45',               # .NET Extensibility 4.x
    'Web-ISAPI-Ext',               # ISAPI Extensions
    'Web-ISAPI-Filter',            # ISAPI Filters
    'Web-Mgmt-Console',            # IIS Management Console
    'Web-Mgmt-Service',            # Web Management Service (WMSVC) ← CLAVE
    'Web-Scripting-Tools'          # IIS Management Scripts and Tools
) -IncludeManagementTools

Write-Host "✅ Features de IIS instaladas" -ForegroundColor Green

# ── 2. ASP.NET Core Hosting Bundle ──────────────────────────────────────────
Write-Host "`n[2/5] Descargando ASP.NET Core Hosting Bundle..." -ForegroundColor Yellow

# NOTA: Verificar la versión más reciente en https://dotnet.microsoft.com/download/dotnet
$hostingBundleUrl = "https://download.visualstudio.microsoft.com/download/pr/hosting-bundle/dotnet-hosting-latest-win.exe"
$hostingBundlePath = "$env:TEMP\dotnet-hosting-bundle.exe"

try {
    Invoke-WebRequest -Uri $hostingBundleUrl -OutFile $hostingBundlePath -UseBasicParsing
    Write-Host "Instalando Hosting Bundle (silencioso)..."
    Start-Process -FilePath $hostingBundlePath -ArgumentList "/install /quiet /norestart" -Wait
    Write-Host "✅ ASP.NET Core Hosting Bundle instalado" -ForegroundColor Green
} catch {
    Write-Warning "⚠️ No se pudo descargar el Hosting Bundle automáticamente."
    Write-Warning "   Descargar manualmente desde: https://dotnet.microsoft.com/download/dotnet"
}

# ── 3. Web Deploy 3.6 ──────────────────────────────────────────────────────
Write-Host "`n[3/5] Instalando Web Deploy 3.6..." -ForegroundColor Yellow

$webDeployUrl = "https://download.microsoft.com/download/0/1/D/01DC28EA-638C-4A22-A57B-4CEF97755C6C/WebDeploy_amd64_en-US.msi"
$webDeployPath = "$env:TEMP\WebDeploy_amd64.msi"

Invoke-WebRequest -Uri $webDeployUrl -OutFile $webDeployPath -UseBasicParsing

# Instalar con TODAS las features (incluyendo Web Deploy Handler)
Start-Process msiexec.exe -ArgumentList @(
    "/i", "`"$webDeployPath`"",
    "/quiet",
    "/norestart",
    "ADDLOCAL=ALL"       # ← Instala TODAS las features incluyendo el Handler
) -Wait

# Verificar instalación
$msdeployExe = "C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe"
if (Test-Path $msdeployExe) {
    $version = (Get-Item $msdeployExe).VersionInfo.ProductVersion
    Write-Host "✅ Web Deploy $version instalado en: $msdeployExe" -ForegroundColor Green
} else {
    throw "ERROR: msdeploy.exe no encontrado después de la instalación"
}

# ── 4. Habilitar Web Management Service ─────────────────────────────────────
Write-Host "`n[4/5] Configurando Web Management Service (WMSVC)..." -ForegroundColor Yellow

# Habilitar conexiones remotas (necesario incluso para localhost)
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\WebManagement\Server" `
    -Name "EnableRemoteManagement" -Value 1

# Configurar autenticación Windows
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\WebManagement\Server" `
    -Name "RequiresWindowsCredentials" -Value 1

# Configurar inicio automático
Set-Service -Name WMSvc -StartupType Automatic
Start-Service WMSvc

Write-Host "✅ WMSVC habilitado y corriendo (puerto 8172)" -ForegroundColor Green

# ── 5. Verificación ─────────────────────────────────────────────────────────
Write-Host "`n[5/5] Verificación final..." -ForegroundColor Yellow

# Verificar IIS
$iisFeatures = Get-WindowsFeature Web-* | Where-Object Installed
Write-Host "  Features IIS instaladas: $($iisFeatures.Count)"

# Verificar WMSVC
$wmsvc = Get-Service WMSvc
Write-Host "  WMSVC Estado: $($wmsvc.Status)"
Write-Host "  WMSVC Startup: $($wmsvc.StartType)"

# Verificar Web Deploy
Write-Host "  MSDeploy: $(if (Test-Path $msdeployExe) { 'OK' } else { 'NO ENCONTRADO' })"

# Verificar puerto 8172
$listener = Get-NetTCPConnection -LocalPort 8172 -ErrorAction SilentlyContinue
Write-Host "  Puerto 8172: $(if ($listener) { 'ESCUCHANDO' } else { 'NO DISPONIBLE' })"

# Verificar que NO hay Visual Studio / Build Tools
$vs = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\VisualStudio\*" -ErrorAction SilentlyContinue
if ($vs) { Write-Warning "⚠️ Visual Studio detectado — DEBE ser removido del servidor" }

Write-Host "`n=========================================" -ForegroundColor Green
Write-Host "  INSTALACIÓN COMPLETADA"
Write-Host "=========================================" -ForegroundColor Green
Write-Host "Siguiente paso: Ejecutar el script de bootstrap (Sección 8)"
```

> ⚠️ **IMPORTANTE:** Es obligatorio instalar Web Deploy con `ADDLOCAL=ALL` para incluir el Web Deploy Handler. Si se instala con opciones por defecto, el Handler no se incluye y la delegación no funcionará.

### 2.3 Verificar que NO Debe Estar Instalado

El servidor de deploy **NO debe tener**:

- ❌ Visual Studio
- ❌ Build Tools
- ❌ SDKs de compilación (.NET SDK)

Solo debe tener el **runtime** (Hosting Bundle) y las herramientas de deploy.

---

## 3. Cuenta de Servicio y Permisos (Mínimo Privilegio)

### 3.1 Principio Fundamental

> ✅ **Con Web Deploy + WMSVC, la cuenta de servicio NO necesita ser administrador local.**
> El servicio WMSVC actúa como intermediario privilegiado. La cuenta solo necesita:
> 1. Permisos NTFS sobre las carpetas de deploy (con herencia)
> 2. Delegación de Web Deploy sobre los sitios específicos
> 3. Pertenecer al grupo `Users` local (por defecto al crearla)

### 3.2 Crear la Cuenta de Servicio

```powershell
# Ejecutar como Administrador
$password = Read-Host -AsSecureString "Ingrese contraseña para svc_ado_deploy"

New-LocalUser -Name "svc_ado_deploy" `
    -Password $password `
    -Description "Cuenta de servicio para Azure DevOps Agent (deploy IIS via Web Deploy)" `
    -PasswordNeverExpires $true `
    -UserMayNotChangePassword $true

# Solo grupo Users — NO Administrators
Add-LocalGroupMember -Group "Users" -Member "svc_ado_deploy"
```

### 3.3 Permisos NTFS con Herencia

Los permisos se configuran **en los folders padre** para que se hereden automáticamente a todas las aplicaciones presentes y futuras. Esto es clave para que el primer deploy funcione sin intervención.

```powershell
# Ejecutar como Administrador — UNA SOLA VEZ

$account = "svc_ado_deploy"

# ── 1. Carpeta raíz de IIS — hereda a TODAS las apps ────────────────────
#    Cubre: WeatherApp, HRPortal, FuturaApp, etc.
icacls "C:\inetpub\wwwroot" `
    /grant "${account}:(OI)(CI)M" `
    /T
Write-Host "✅ Modify heredado en C:\inetpub\wwwroot"

# ── 2. Carpeta raíz de backups ──────────────────────────────────────────
New-Item -ItemType Directory -Path "C:\deploy-backups" -Force | Out-Null
icacls "C:\deploy-backups" `
    /grant "${account}:(OI)(CI)M" `
    /T
Write-Host "✅ Modify heredado en C:\deploy-backups"

# ── 3. Carpeta del agente Azure DevOps ──────────────────────────────────
icacls "C:\agent" `
    /grant "${account}:(OI)(CI)F" `
    /T
Write-Host "✅ Full Control en C:\agent"
```

**Referencia de flags `icacls`:**

| Flag | Significado |
|------|-------------|
| `(OI)` | Object Inherit — aplica a archivos dentro |
| `(CI)` | Container Inherit — aplica a subcarpetas |
| `M` | Modify — leer, escribir, eliminar (sin cambiar permisos ni propietario) |
| `F` | Full Control — todo |
| `/T` | Aplica recursivamente a contenido existente |

### 3.4 Tabla Resumen de Permisos

| Recurso | Permiso | Hereda a apps futuras | Método |
|---------|---------|----------------------|--------|
| `C:\inetpub\wwwroot\` | Modify | ✅ Sí | `icacls (OI)(CI)M` |
| `C:\deploy-backups\` | Modify | ✅ Sí | `icacls (OI)(CI)M` |
| `C:\agent\` | Full Control | N/A | `icacls (OI)(CI)F` |
| Sitios IIS (Stop/Start/Deploy) | Delegado via WMSVC | ✅ Sí (por regla) | Web Deploy Delegation Rules |
| Grupo `Administrators` | ❌ NO requerido | N/A | — |

### 3.5 Privilegios de Log On

La cuenta necesita el privilegio **"Log on as a service"** para ejecutar el agente como servicio Windows. Este se configura automáticamente al instalar el agente con `config.cmd`.

Si necesitas configurarlo manualmente:

```
secpol.msc
→ Local Policies → User Rights Assignment
→ "Log on as a service" → Add User → svc_ado_deploy
```

---

## 4. Configuración de Web Management Service (WMSVC)

### 4.1 ¿Qué es WMSVC?

El **Web Management Service** (WMSVC, puerto 8172) es el componente que permite:

1. Administración remota de IIS (via IIS Manager)
2. **Delegación de Web Deploy** — permite que usuarios sin admin ejecuten `msdeploy` sobre sitios específicos

WMSVC corre como `Local Service` con privilegios suficientes para gestionar IIS. Actúa como **proxy** entre la cuenta no-admin y IIS.

### 4.2 Configuración via GUI (IIS Manager)

1. Abrir **IIS Manager** (`inetmgr`)
2. Seleccionar el **nodo del servidor** (raíz, panel izquierdo)
3. Doble clic en **"Management Service"** (panel central)
4. Configurar:

```
☑ Enable remote connections
Identity credentials:
  ● Windows credentials          ← Seleccionar esta opción
```

5. Clic en **"Apply"** (panel derecho)
6. Clic en **"Start"** si el servicio está detenido

### 4.3 Configuración via PowerShell (recomendado para automatizar)

```powershell
# Ya incluido en el script de instalación (Sección 2)
# Aquí se muestra por referencia

# Habilitar administración remota
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\WebManagement\Server" `
    -Name "EnableRemoteManagement" -Value 1

# Usar credenciales Windows
Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\WebManagement\Server" `
    -Name "RequiresWindowsCredentials" -Value 1

# Inicio automático
Set-Service -Name WMSvc -StartupType Automatic
Restart-Service WMSvc
```

### 4.4 Verificar WMSVC

```powershell
# Estado del servicio
Get-Service WMSvc | Format-Table Name, Status, StartType

# Puerto escuchando
Get-NetTCPConnection -LocalPort 8172 -State Listen

# Registro de eventos
Get-EventLog -LogName Application -Source "Web Management Service" -Newest 5
```

---

## 5. Delegación de Web Deploy por Sitio

### 5.1 Concepto de Delegación

La delegación permite que un usuario sin admin pueda ejecutar `msdeploy` contra un sitio específico. El flujo es:

```
svc_ado_deploy (sin admin)
    → msdeploy -dest:auto,computerName="https://localhost:8172/msdeploy.axd"
        → WMSVC verifica: ¿tiene este usuario delegación sobre este sitio?
            → Sí → WMSVC ejecuta la operación como intermediario privilegiado
            → No → 401 Unauthorized
```

### 5.2 Configurar Reglas de Delegación via GUI

1. En **IIS Manager**, seleccionar el **nodo del servidor**
2. Doble clic en **"Management Service Delegation"**
3. En el **panel derecho**, clic en **"Add Rule..."**
4. Seleccionar **"Deploy Applications with Content"**
5. Clic en **"OK"**

Esto crea una regla que permite a usuarios autorizados desplegar contenido en los sitios que se les haya asignado.

### 5.3 Asignar Permiso de Deploy al Usuario por Sitio

Para cada sitio IIS que la cuenta debe poder desplegar:

**Via GUI:**

1. En IIS Manager, expandir **Sites**
2. Clic en el sitio (ej: `WeatherApp`)
3. En el panel central, doble clic en **"IIS Manager Permissions"**
4. Panel derecho → **"Allow User..."**
5. Seleccionar: **Windows** → escribir `svc_ado_deploy` → **OK**

**Via PowerShell (recomendado):**

```powershell
# Función reutilizable para delegar un sitio a un usuario
function Grant-WebDeployAccess {
    param(
        [Parameter(Mandatory)] [string] $SiteName,
        [Parameter(Mandatory)] [string] $UserName
    )

    $appcmd = "$env:WinDir\System32\inetsrv\appcmd.exe"

    # Verificar que el sitio existe
    $siteExists = & $appcmd list site /name:"$SiteName" 2>$null
    if (-not $siteExists) {
        throw "El sitio '$SiteName' no existe en IIS"
    }

    # Agregar permiso de IIS Manager
    & $appcmd set config -section:system.webServer/management/authorization `
        /+"[name='$SiteName',accessType='Allow',users='$UserName']" `
        /commit:apphost

    Write-Host "✅ Usuario '$UserName' autorizado para desplegar en '$SiteName'"
}

# Ejemplo de uso:
Grant-WebDeployAccess -SiteName "WeatherApp-Test" -UserName "svc_ado_deploy"
Grant-WebDeployAccess -SiteName "WeatherApp-Prod" -UserName "svc_ado_deploy"
```

### 5.4 Configurar Reglas de Delegación de Providers

Las reglas de delegación determinan qué operaciones puede hacer un usuario delegado:

```powershell
# Ejecutar como Administrador — configuración global del servidor

$appcmd = "$env:WinDir\System32\inetsrv\appcmd.exe"

# Regla 1: Permitir deploy de contenido (contentPath)
& $appcmd set config -section:system.webServer/management/delegation `
    /+"[name='contentPath',accessType='Allow']" `
    /commit:apphost

# Regla 2: Permitir uso de app_offline (appOffline)
& $appcmd set config -section:system.webServer/management/delegation `
    /+"[name='appOffline',accessType='Allow']" `
    /commit:apphost

# Regla 3: Permitir sync de App Pool (recycleApp) — para reiniciar el pool
& $appcmd set config -section:system.webServer/management/delegation `
    /+"[name='recycleApp',accessType='Allow']" `
    /commit:apphost

Write-Host "✅ Reglas de delegación configuradas"
```

### 5.5 Verificar Delegación

```powershell
# Listar reglas de delegación
& "$env:WinDir\System32\inetsrv\appcmd.exe" list config `
    -section:system.webServer/management/delegation

# Listar autorizaciones por sitio
& "$env:WinDir\System32\inetsrv\appcmd.exe" list config `
    -section:system.webServer/management/authorization

# Probar deploy como la cuenta de servicio (sin admin)
# Abrir PowerShell como svc_ado_deploy y ejecutar:
& "C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe" `
    -verb:dump `
    -source:contentPath="Default Web Site" `
    -dest:auto,computerName="https://localhost:8172/msdeploy.axd",`
          userName="svc_ado_deploy",password="PASSWORD",`
          authType="Windows"
# Si funciona → deploy delegado OK
# Si retorna 401 → falta delegación
```

---

## 6. Primer Deploy Automático (Sitio No Existe)

### 6.1 El Problema

En el primer deploy, el sitio IIS no existe aún. `msdeploy` solo puede desplegar sobre sitios existentes — no puede crearlos. Hay dos soluciones:

### 6.2 Solución: Script Bootstrap Idempotente

Se ejecuta **automáticamente como parte del pipeline**, pero el step de creación del sitio se ejecuta con un script pre-configurado en el servidor que **solo necesita ser admin la primera vez**.

La mejor práctica es:

1. Ejecutar el **script bootstrap** (Sección 8) una vez como Admin al preparar el servidor
2. El script crea **todos los sitios IIS y App Pools necesarios** (aunque estén vacíos)
3. El pipeline despliega contenido via Web Deploy — sin admin

### 6.3 Alternativa: Creación Condicional en el Pipeline

Si prefieres que el pipeline cree el sitio en el primer deploy, se puede hacer con un step condicional. Esto **requiere** que el agente tenga la capacidad de ejecutar `appcmd` (lo cual requiere acceso a `inetsrv`):

```yaml
# Step condicional — solo corre si el sitio no existe
- powershell: |
    $appcmd = "$env:WinDir\System32\inetsrv\appcmd.exe"
    $site   = "${{ parameters.websiteName }}"
    $pool   = "${{ parameters.appPoolName }}"
    $path   = "${{ parameters.deployPath }}"

    $siteExists = & $appcmd list site /name:"$site" 2>$null
    if (-not $siteExists) {
        Write-Host "##vso[task.logissue type=warning]Sitio '$site' no existe."
        Write-Host "Ejecutar bootstrap/setup-iis-site.ps1 como Administrador."
        Write-Host "Ver: DevOps/guia-web-deploy-iis.md — Sección 8"
        exit 1
    }
    Write-Host "✅ Sitio '$site' existe"
  displayName: 'Verificar sitio IIS existe'
```

> 📋 **Recomendación:** Usar el **script bootstrap** (Sección 8) como parte de la preparación del servidor. Es más limpio que darle permisos de creación al agente.

---

## 7. Servidor con Múltiples Aplicaciones

### 7.1 Modelo de Permisos

Con Web Deploy + herencia NTFS, una **sola cuenta** puede desplegar **múltiples aplicaciones** de forma segura:

```
svc_ado_deploy (sin admin)
    │
    ├── C:\inetpub\wwwroot\WeatherApp\     ← herencia de wwwroot (Modify)
    ├── C:\inetpub\wwwroot\HRPortal\       ← herencia de wwwroot (Modify)
    ├── C:\inetpub\wwwroot\FinanceApp\     ← herencia de wwwroot (Modify)
    │
    ├── Deploy WeatherApp via WMSVC        ← delegación por sitio
    ├── Deploy HRPortal via WMSVC          ← delegación por sitio
    └── Deploy FinanceApp via WMSVC        ← delegación por sitio
```

### 7.2 Configuración para Cada App Nueva

Cuando se agrega una nueva aplicación al servidor, solo se necesita:

1. **Crear el sitio IIS** (vía bootstrap script, como Admin)
2. **Delegar el sitio** a la cuenta de servicio (una línea de PowerShell)

Los permisos de carpeta **se heredan automáticamente** de `C:\inetpub\wwwroot`.

```powershell
# Para cada nueva app — ejecutar como Admin UNA VEZ
$siteName    = "NuevaApp"
$poolName    = "NuevaApp-Pool"
$physicalPath = "C:\inetpub\wwwroot\NuevaApp"
$port         = 80
$hostHeader   = "nuevaapp.empresa.local"
$account      = "svc_ado_deploy"

# 1. Crear directorio (hereda permisos de wwwroot automáticamente)
New-Item -ItemType Directory -Path $physicalPath -Force

# 2. Crear App Pool
& "$env:WinDir\System32\inetsrv\appcmd.exe" add apppool `
    /name:"$poolName" `
    /managedRuntimeVersion:"" `
    /managedPipelineMode:Integrated

# 3. Crear Sitio
& "$env:WinDir\System32\inetsrv\appcmd.exe" add site `
    /name:"$siteName" `
    /physicalPath:"$physicalPath" `
    /bindings:"http/*:${port}:${hostHeader}"

& "$env:WinDir\System32\inetsrv\appcmd.exe" set site `
    /site.name:"$siteName" `
    "/[path='/'].applicationPool:$poolName"

# 4. Delegar deploy a la cuenta
Grant-WebDeployAccess -SiteName $siteName -UserName $account

Write-Host "✅ App '$siteName' lista para deploy via Web Deploy"
```

### 7.3 Modelo de Seguridad: Una Cuenta vs. Múltiples Cuentas

| Escenario | Recomendación | Motivo |
|-----------|---------------|--------|
| Todas las apps son del mismo equipo | ✅ Una cuenta `svc_ado_deploy` | Simple, mismo nivel de confianza |
| Apps de equipos distintos | ⚠️ Una cuenta por equipo (`svc_deploy_teamA`, `svc_deploy_teamB`) | Aislamiento entre equipos |
| Apps con requisitos de compliance distintos | ✅ Una cuenta por clasificación | Segregación de responsabilidades |

Con múltiples cuentas, cada una solo se delega sobre sus sitios específicos:

```powershell
# Equipo A solo puede desplegar sus apps
Grant-WebDeployAccess -SiteName "WeatherApp" -UserName "svc_deploy_teamA"
Grant-WebDeployAccess -SiteName "HRPortal"   -UserName "svc_deploy_teamA"

# Equipo B solo puede desplegar sus apps
Grant-WebDeployAccess -SiteName "FinanceApp"  -UserName "svc_deploy_teamB"
Grant-WebDeployAccess -SiteName "InternalAPI"  -UserName "svc_deploy_teamB"
```

---

## 8. Script Bootstrap Completo

Este script prepara un servidor desde cero. Se ejecuta **una sola vez como Administrador** al comisionar un nuevo servidor o al agregar nuevas aplicaciones.

### 8.1 Archivo: `bootstrap/setup-iis-server.ps1`

```powershell
# =============================================================================
# Script Bootstrap: Preparar servidor IIS para deploys con Web Deploy
# =============================================================================
# REQUISITO: Ejecutar como Administrador
# REQUISITO: Ejecutar DESPUÉS del script de instalación (Sección 2)
# USO:       .\setup-iis-server.ps1 -ConfigFile .\server-config.json
# =============================================================================

param(
    [Parameter(Mandatory)]
    [string] $ConfigFile
)

# Verificar ejecución como admin
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Este script debe ejecutarse como Administrador"
}

# Leer configuración
$config = Get-Content $ConfigFile -Raw | ConvertFrom-Json

$appcmd  = "$env:WinDir\System32\inetsrv\appcmd.exe"
$account = $config.serviceAccount

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  BOOTSTRAP DEL SERVIDOR IIS"
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Cuenta de servicio: $account"
Write-Host "Aplicaciones a configurar: $($config.sites.Count)"

# ── 1. Verificar prerrequisitos ──────────────────────────────────────────
Write-Host "`n[1/5] Verificando prerrequisitos..." -ForegroundColor Yellow

$msdeployExe = "C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe"
if (-not (Test-Path $msdeployExe)) {
    throw "Web Deploy no instalado. Ejecutar primero el script de instalación (Sección 2)"
}

$wmsvc = Get-Service WMSvc -ErrorAction SilentlyContinue
if (-not $wmsvc -or $wmsvc.Status -ne "Running") {
    throw "WMSVC no está corriendo. Ejecutar primero el script de instalación (Sección 2)"
}

# Verificar que la cuenta existe
try {
    $null = Get-LocalUser -Name ($account -replace '.*\\', '')
} catch {
    throw "La cuenta '$account' no existe. Crearla primero (Sección 3.2)"
}

Write-Host "✅ Prerrequisitos OK" -ForegroundColor Green

# ── 2. Configurar permisos NTFS con herencia ────────────────────────────
Write-Host "`n[2/5] Configurando permisos NTFS..." -ForegroundColor Yellow

$paths = @(
    @{ Path = "C:\inetpub\wwwroot";   Permission = "M" },
    @{ Path = "C:\deploy-backups";     Permission = "M" },
    @{ Path = "C:\agent";              Permission = "F" }
)

foreach ($p in $paths) {
    if (-not (Test-Path $p.Path)) {
        New-Item -ItemType Directory -Path $p.Path -Force | Out-Null
    }
    icacls $p.Path /grant "${account}:(OI)(CI)$($p.Permission)" /T /Q
    Write-Host "  ✅ $($p.Path) → $($p.Permission)"
}

# ── 3. Configurar reglas de delegación de Web Deploy ────────────────────
Write-Host "`n[3/5] Configurando reglas de delegación..." -ForegroundColor Yellow

# Habilitar providers necesarios para deploy
$providers = @("contentPath", "iisApp", "appOffline", "recycleApp", "setAcl")
foreach ($provider in $providers) {
    try {
        & $appcmd set config -section:system.webServer/management/delegation `
            /+"[name='$provider',accessType='Allow']" `
            /commit:apphost 2>$null
        Write-Host "  ✅ Provider '$provider' delegado"
    } catch {
        Write-Host "  ℹ️ Provider '$provider' ya configurado"
    }
}

# ── 4. Crear App Pools y Sitios IIS ────────────────────────────────────
Write-Host "`n[4/5] Creando sitios IIS..." -ForegroundColor Yellow

foreach ($site in $config.sites) {
    $siteName  = $site.name
    $poolName  = $site.appPool
    $path      = $site.physicalPath
    $port      = if ($site.port) { $site.port } else { 80 }
    $hostHeader = if ($site.hostHeader) { $site.hostHeader } else { "" }
    $runtime    = if ($site.managedRuntime) { $site.managedRuntime } else { "" }

    Write-Host "`n  Configurando: $siteName" -ForegroundColor White

    # Crear directorio (hereda permisos de wwwroot)
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        Write-Host "    ✅ Carpeta creada: $path"
    }

    # Crear App Pool (idempotente)
    $poolExists = & $appcmd list apppool /name:"$poolName" 2>$null
    if (-not $poolExists) {
        & $appcmd add apppool /name:"$poolName" `
            /managedRuntimeVersion:"$runtime" `
            /managedPipelineMode:Integrated | Out-Null
        & $appcmd set apppool /apppool.name:"$poolName" `
            /processModel.identityType:ApplicationPoolIdentity | Out-Null
        Write-Host "    ✅ App Pool '$poolName' creado (runtime: $(if ($runtime) { $runtime } else { 'No Managed Code' }))"
    } else {
        Write-Host "    ℹ️ App Pool '$poolName' ya existe"
    }

    # Crear Sitio (idempotente)
    $siteExists = & $appcmd list site /name:"$siteName" 2>$null
    if (-not $siteExists) {
        $binding = "http/*:${port}:${hostHeader}"
        & $appcmd add site /name:"$siteName" `
            /physicalPath:"$path" `
            /bindings:"$binding" | Out-Null
        & $appcmd set site /site.name:"$siteName" `
            "/[path='/'].applicationPool:$poolName" | Out-Null
        Write-Host "    ✅ Sitio '$siteName' creado ($binding)"
    } else {
        Write-Host "    ℹ️ Sitio '$siteName' ya existe"
    }

    # Delegar deploy al usuario
    try {
        & $appcmd set config -section:system.webServer/management/authorization `
            /+"[name='$siteName',accessType='Allow',users='$account']" `
            /commit:apphost 2>$null
        Write-Host "    ✅ Deploy delegado a '$account'"
    } catch {
        Write-Host "    ℹ️ Delegación ya existe"
    }

    # Crear carpeta de backup
    $backupDir = "C:\deploy-backups\$siteName"
    if (-not (Test-Path $backupDir)) {
        New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        Write-Host "    ✅ Carpeta backup: $backupDir"
    }
}

# ── 5. Resumen ──────────────────────────────────────────────────────────
Write-Host "`n[5/5] Resumen..." -ForegroundColor Yellow

Write-Host ""
Write-Host "=========================================" -ForegroundColor Green
Write-Host "  BOOTSTRAP COMPLETADO"
Write-Host "=========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Sitios configurados:"
foreach ($site in $config.sites) {
    $state = (& $appcmd list site /name:"$($site.name)" /text:state 2>$null)
    Write-Host "  • $($site.name) — Estado: $state"
}
Write-Host ""
Write-Host "Cuenta de servicio: $account"
Write-Host "Admin local requerido: NO"
Write-Host "Siguiente paso: Instalar agente Azure DevOps con esta cuenta"
```

### 8.2 Archivo de Configuración: `bootstrap/server-config.json`

```json
{
    "serviceAccount": "svc_ado_deploy",
    "sites": [
        {
            "name": "WeatherApp-Test",
            "appPool": "WeatherApp-Test-Pool",
            "physicalPath": "C:\\inetpub\\wwwroot\\WeatherApp",
            "port": 80,
            "hostHeader": "weatherapp-test.empresa.local",
            "managedRuntime": ""
        },
        {
            "name": "WeatherApp-Prod",
            "appPool": "WeatherApp-Prod-Pool",
            "physicalPath": "C:\\inetpub\\wwwroot\\WeatherApp",
            "port": 80,
            "hostHeader": "weatherapp.empresa.local",
            "managedRuntime": ""
        }
    ]
}
```

> 📋 **Para agregar una nueva app:** Solo agrega un nuevo objeto al array `sites` y vuelve a ejecutar el script. Es idempotente — no duplica ni rompe los sitios existentes.

### 8.3 Ejemplo para .NET Framework 4.x

```json
{
    "name": "LegacyApp-Test",
    "appPool": "LegacyApp-Test-Pool",
    "physicalPath": "C:\\inetpub\\wwwroot\\LegacyApp",
    "port": 80,
    "hostHeader": "legacyapp-test.empresa.local",
    "managedRuntime": "v4.0"
}
```

> 📋 **`managedRuntime`:** Usar `""` (vacío) para .NET Core / .NET 5+. Usar `"v4.0"` para .NET Framework 4.x.

---

## 9. Integración con Azure DevOps Pipelines

### 9.1 Cómo el Pipeline Usa Web Deploy

El template `cd-deploy-iis.yml` ejecuta `msdeploy.exe` localmente. El agente self-hosted corre en el **mismo servidor que IIS**, por lo que la conexión es hacia `localhost:8172`.

### 9.2 Comando MSDeploy en el Pipeline

```yaml
- powershell: |
    $msdeployExe = "C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe"
    $site        = "${{ parameters.websiteName }}"
    $artifactPath = "$(Pipeline.Workspace)/${{ parameters.artifactName }}-$(Build.BuildNumber)-$(Build.SourceBranchName)"

    Write-Host "Desplegando con Web Deploy..."
    Write-Host "  Sitio:     $site"
    Write-Host "  Artefacto: $artifactPath"

    & $msdeployExe `
        -verb:sync `
        -source:contentPath="$artifactPath" `
        -dest:contentPath="$site",`
              computerName="https://localhost:8172/msdeploy.axd",`
              userName="$(WebDeployUser)",`
              password="$(WebDeployPassword)",`
              authType="Basic" `
        -enableRule:AppOffline `
        -allowUntrusted `
        -retryAttempts:2 `
        -retryInterval:3000

    if ($LASTEXITCODE -ne 0) {
        throw "Web Deploy falló con código: $LASTEXITCODE"
    }
    Write-Host "✅ Deploy completado"
  displayName: 'Deploy con Web Deploy'
  env:
    WebDeployUser: $(WebDeployUser)
    WebDeployPassword: $(WebDeployPassword)
```

### 9.3 Variables Necesarias en el Variable Group

Además de las variables existentes, agregar al Variable Group:

| Variable | Valor | ¿Secreto? |
|----------|-------|-----------|
| `WebDeployUser` | `svc_ado_deploy` | No |
| `WebDeployPassword` | Contraseña de la cuenta | 🔒 Sí |

### 9.4 Explicación de Parámetros de MSDeploy

| Parámetro | Significado |
|-----------|-------------|
| `-verb:sync` | Sincronizar origen → destino (solo archivos cambiados) |
| `-source:contentPath="..."` | Origen: carpeta del artefacto descargado |
| `-dest:contentPath="SiteName"` | Destino: nombre del sitio IIS |
| `computerName="https://localhost:8172/msdeploy.axd"` | Conexión vía WMSVC local |
| `authType="Basic"` | Autenticación con usuario/password |
| `-enableRule:AppOffline` | Crea `app_offline.htm` automáticamente durante el deploy |
| `-allowUntrusted` | Permite certificados self-signed (localhost) |
| `-retryAttempts:2` | Reintentar 2 veces si falla |

---

## 10. Troubleshooting

### 10.1 Errores Comunes

| Error | Causa | Solución |
|-------|-------|---------|
| `Could not connect to the remote computer (localhost:8172)` | WMSVC no está corriendo | `Start-Service WMSvc` → Verificar con `Get-Service WMSvc` |
| `401 Unauthorized` usando msdeploy | Cuenta no tiene delegación sobre el sitio | Ejecutar `Grant-WebDeployAccess` (Sección 5.3) para el sitio |
| `The remote name could not be resolved: 'localhost'` | Problema de resolución DNS | Usar `127.0.0.1` en lugar de `localhost` en `computerName` |
| `ERROR_INSUFFICIENT_ACCESS_TO_SITE_FOLDER` | Permisos NTFS faltantes en carpeta del sitio | Verificar herencia de `C:\inetpub\wwwroot` con `icacls` |
| `Web Deploy is not installed or the component is missing` | Web Deploy instalado sin Handler | Reinstalar con `ADDLOCAL=ALL` (Sección 2.2) |
| `The application pool 'X' does not exist` | Sitio no fue bootstrapeado | Ejecutar script bootstrap (Sección 8) |
| `ERROR_DESTINATION_NOT_REACHABLE` | Puerto 8172 bloqueado por firewall | Agregar regla: `New-NetFirewallRule -Name "WMSVC" -DisplayName "Web Management Service" -Direction Inbound -LocalPort 8172 -Protocol TCP -Action Allow` |
| `redirection.config - permisos insuficientes` | Usando `Get-Website` o `WebAdministration` en vez de msdeploy | Usar `msdeploy.exe` via WMSVC — no usar `Import-Module WebAdministration` directamente |
| `The content path does not exist` | Primer deploy, carpeta no creada | Ejecutar bootstrap (Sección 8) o crear carpeta manualmente |

### 10.2 Comandos de Diagnóstico

```powershell
# ── Estado del servidor ─────────────────────────────────────────────────
# WMSVC
Get-Service WMSvc | Format-Table Name, Status, StartType
Get-NetTCPConnection -LocalPort 8172 -State Listen -ErrorAction SilentlyContinue

# Web Deploy instalado
& "C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe" -? 2>&1 | Select-Object -First 1

# ── Permisos ────────────────────────────────────────────────────────────
# Verificar permisos NTFS
icacls "C:\inetpub\wwwroot" | Select-String "svc_ado_deploy"
icacls "C:\deploy-backups"  | Select-String "svc_ado_deploy"

# ── Delegación ──────────────────────────────────────────────────────────
# Listar reglas de delegación
& "$env:WinDir\System32\inetsrv\appcmd.exe" list config `
    -section:system.webServer/management/authorization

# ── Test de deploy (dry run) ────────────────────────────────────────────
# Prueba sin modificar nada (-whatif)
& "C:\Program Files\IIS\Microsoft Web Deploy V3\msdeploy.exe" `
    -verb:sync `
    -source:contentPath="C:\temp\test-folder" `
    -dest:contentPath="WeatherApp-Test",`
          computerName="https://localhost:8172/msdeploy.axd",`
          userName="svc_ado_deploy",password="PASSWORD",`
          authType="Basic" `
    -whatif `
    -allowUntrusted

# ── Logs de WMSVC ───────────────────────────────────────────────────────
Get-EventLog -LogName Application -Source "Web Management Service" -Newest 10 |
    Format-Table TimeGenerated, EntryType, Message -AutoSize -Wrap

# ── Logs de Web Deploy ──────────────────────────────────────────────────
Get-Content "$env:ProgramFiles\IIS\Microsoft Web Deploy V3\MsDeployLogs\*.log" -Tail 20
```

---

## 11. Checklist de Verificación

### 11.1 Preparación del Servidor (una sola vez)

- [ ] IIS instalado con features de gestión (`Web-Mgmt-Service`, `Web-Scripting-Tools`)
- [ ] ASP.NET Core Hosting Bundle instalado (si aplica)
- [ ] Web Deploy 3.6 instalado con `ADDLOCAL=ALL`
- [ ] WMSVC habilitado, corriendo y en inicio automático
- [ ] Puerto 8172 escuchando (`Get-NetTCPConnection -LocalPort 8172`)
- [ ] Cuenta `svc_ado_deploy` creada (grupo Users, NO Administrators)
- [ ] Permisos NTFS con herencia en `C:\inetpub\wwwroot` y `C:\deploy-backups`
- [ ] Reglas de delegación de Web Deploy configuradas
- [ ] Script bootstrap ejecutado con archivo de configuración
- [ ] Todos los sitios creados y delegados a la cuenta
- [ ] **NO hay** Visual Studio ni Build Tools en el servidor

### 11.2 Por Cada Nueva Aplicación

- [ ] Agregar entrada al `server-config.json`
- [ ] Re-ejecutar script bootstrap (es idempotente)
- [ ] Verificar que el sitio aparece en IIS Manager
- [ ] Verificar delegación: `appcmd list config -section:management/authorization`
- [ ] Agregar Variable Group correspondiente en Azure DevOps (`WebDeployUser`, `WebDeployPassword`)
- [ ] Crear el pipeline YAML apuntando al sitio

### 11.3 Verificación Post-Deploy

- [ ] `msdeploy -verb:dump` funciona contra el sitio (sin admin)
- [ ] Pipeline CD ejecuta sin errores
- [ ] Health check pasa
- [ ] `app_offline.htm` fue removido automáticamente después del deploy
- [ ] Archivos correctos en `C:\inetpub\wwwroot\<app>\`

---

**Fin de la Guía**

*Versión 1.0 — Marzo 2026*  
*Complemento de: Manual de Configuración de Azure DevOps (manual-azure-devops.md)*
