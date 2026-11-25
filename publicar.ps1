# Publica uma versao nova: compila, gera o instalador e cria a release no
# GitHub, de onde as outras maquinas se atualizam.
#
#   .\publicar.ps1 -Versao 1.0.0
#   .\publicar.ps1 -Versao 1.0.1 -SemRelease    so gera o instalador em dist\
#
# A release vira a fonte de atualizacao: na outra maquina, baixe o instalador
# mais novo e execute. Ele fecha a ilha, substitui o executavel e mantem a
# configuracao, que vive em %APPDATA% e nao no diretorio do app.

param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [switch]$SemRelease
)

$ErrorActionPreference = 'Stop'

if ($Versao -notmatch '^\d+\.\d+\.\d+$') {
    throw "Versao deve ser no formato 1.2.3, recebi '$Versao'."
}

$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) {
    throw "Inno Setup nao encontrado em $iscc. Instale com: winget install JRSoftware.InnoSetup"
}

$raiz = $PSScriptRoot
Set-Location $raiz

Write-Host "== fechando a ilha ==" -ForegroundColor Cyan
Get-Process DynamicIsland -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 700

Write-Host "== publicando o executavel ==" -ForegroundColor Cyan
dotnet publish .\DynamicIsland\DynamicIsland.csproj `
    -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true `
    -p:Version=$Versao -o .\publish -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }

Write-Host "== gerando o instalador ==" -ForegroundColor Cyan
& $iscc "/DAppVersion=$Versao" ".\instalador\DynamicIsland.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC falhou." }

$setup = ".\dist\DynamicIsland-$Versao-setup.exe"
if (-not (Test-Path $setup)) { throw "Nao encontrei $setup." }

$mb = [math]::Round((Get-Item $setup).Length / 1MB, 2)
Write-Host "instalador: $setup ($mb MB)" -ForegroundColor Green

if ($SemRelease) {
    Write-Host "`n-SemRelease: parando aqui, sem publicar no GitHub."
    return
}

Write-Host "== criando a release ==" -ForegroundColor Cyan

# A conta ativa do gh alterna entre contas; a release tem de sair na dona do repo.
# gh escreve o aviso de troca no stderr, e com ErrorActionPreference Stop isso aborta.
& { $ErrorActionPreference = 'Continue'; gh auth switch --user Arthur-Rabello 2>&1 | Out-Null }

$tag = "v$Versao"
$existe = & { $ErrorActionPreference = 'Continue'
    gh release view $tag --repo Arthur-Rabello/dynamic-island-windows 2>$null }
if ($LASTEXITCODE -eq 0 -and $existe) { throw "A release $tag ja existe. Use outra versao." }

gh release create $tag $setup `
    --repo Arthur-Rabello/dynamic-island-windows `
    --title "Dynamic Island $Versao" `
    --notes "Instalador para Windows. Baixe o .exe e execute; a configuracao e o login do Google sao preservados."
if ($LASTEXITCODE -ne 0) { throw "gh release create falhou." }

Write-Host "`nPronto. Para atualizar outra maquina:" -ForegroundColor Green
Write-Host "  gh release download $tag --repo Arthur-Rabello/dynamic-island-windows --pattern '*.exe'"
