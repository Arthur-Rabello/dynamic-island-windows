# Liga ou desliga o início automático da Dynamic Island com o Windows.
#
#   .\inicio-automatico.ps1          -> ativa
#   .\inicio-automatico.ps1 -Remover -> desativa
#
# Cria (ou apaga) um atalho na pasta Inicializar do usuário atual.
# Não mexe no registro nem exige administrador.

param([switch]$Remover)

$ErrorActionPreference = 'Stop'

$exe = Join-Path $PSScriptRoot 'publish\DynamicIsland.exe'
$startup = [Environment]::GetFolderPath('Startup')
$atalho = Join-Path $startup 'Dynamic Island.lnk'

if ($Remover) {
    if (Test-Path $atalho) {
        Remove-Item $atalho
        Write-Host "Início automático desativado." -ForegroundColor Yellow
    } else {
        Write-Host "Não havia atalho para remover."
    }
    return
}

if (-not (Test-Path $exe)) {
    throw "Não encontrei $exe. Rode antes: dotnet publish .\DynamicIsland\DynamicIsland.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\publish"
}

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($atalho)
$link.TargetPath = $exe
$link.WorkingDirectory = Split-Path $exe
$link.Description = 'Dynamic Island'
$link.Save()

Write-Host "Início automático ativado." -ForegroundColor Green
Write-Host "Atalho: $atalho"
