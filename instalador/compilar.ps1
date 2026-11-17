# genera el exe portable autocontenido y el instalador de coreblaze en la carpeta ejecutable
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$salida = Join-Path $raiz 'ejecutable'

# lee la version desde el proyecto
[xml]$proyecto = Get-Content (Join-Path $raiz 'CoreBlaze.csproj')
$version = ($proyecto.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
Write-Host "coreblaze $version"

# exe unico autocontenido, no necesita tener .net instalado
dotnet publish (Join-Path $raiz 'CoreBlaze.csproj') -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none -o (Join-Path $salida 'app')
if ($LASTEXITCODE -ne 0) { throw 'fallo dotnet publish' }

Copy-Item (Join-Path $salida 'app\CoreBlaze.exe') (Join-Path $salida "CoreBlaze-$version-portable.exe") -Force

# busca el compilador de inno setup
$iscc = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
  "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'no se encontro inno setup 6, instalalo con: winget install JRSoftware.InnoSetup' }

& $iscc "/DAppVersion=$version" (Join-Path $PSScriptRoot 'CoreBlaze.iss')
if ($LASTEXITCODE -ne 0) { throw 'fallo inno setup' }

Get-ChildItem $salida -File -Filter "CoreBlaze-$version-*" | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
