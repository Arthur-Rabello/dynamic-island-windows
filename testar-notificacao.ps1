# Testa o pop de mensagens da ilha sem depender de alguem te mandar WhatsApp.
#
#   .\testar-notificacao.ps1 -Listar       mostra os apps que notificaram, com o AUMID
#   .\testar-notificacao.ps1               dispara 2 toasts seguidos no app configurado
#   .\testar-notificacao.ps1 -App Discord  dispara em nome de outro app
#
# Os dois toasts seguidos sao o teste que importa: um app de conversa substitui
# a propria notificacao a cada mensagem nova, reaproveitando o mesmo id, e a
# ilha precisa popar nas duas.
#
# A ilha precisa estar aberta ANTES do disparo: a primeira varredura ignora o
# que ja estava na tela, para nao anunciar mensagem velha ao iniciar.

param(
    [string]$App,
    [int]$Vezes = 2,
    [switch]$Listar
)

$ErrorActionPreference = 'Stop'

$null = [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
$null = [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime]

function Get-Toasts {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime -ErrorAction SilentlyContinue
    $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]

    $listener = [Windows.UI.Notifications.Management.UserNotificationListener, Windows.UI.Notifications, ContentType = WindowsRuntime]::Current
    $nType = [Windows.UI.Notifications.UserNotification, Windows.UI.Notifications, ContentType = WindowsRuntime]
    $listType = [System.Collections.Generic.IReadOnlyList`1].MakeGenericType($nType)

    $t = $asTask.MakeGenericMethod($listType).Invoke($null, @(
        $listener.GetNotificationsAsync([Windows.UI.Notifications.NotificationKinds]::Toast)))
    $t.Wait(10000) | Out-Null
    return $t.Result
}

if ($Listar) {
    Write-Host "Apps com notificacao viva agora (use o AUMID no campo MENSAGENS):" -ForegroundColor Cyan
    foreach ($n in Get-Toasts) {
        Write-Host ("  {0,-22} {1}" -f $n.AppInfo.DisplayInfo.DisplayName, $n.AppInfo.AppUserModelId)
    }
    Write-Host ""
    Write-Host "Nada do WhatsApp na lista? Entao o Vivaldi nao esta criando toast nativo:"
    Write-Host "confira em vivaldi://settings, Paginas web, Notificacoes."
    return
}

# Sem -App, usa o que estiver configurado na ilha.
if (-not $App) {
    $cfg = Join-Path $env:APPDATA "DynamicIsland\settings.json"
    if (Test-Path $cfg) { $App = ([IO.File]::ReadAllText($cfg) | ConvertFrom-Json).MessageApp }
}

if (-not $App) {
    throw "Nenhum app configurado. Preencha MENSAGENS nas Configuracoes, ou passe -App."
}

# O filtro da ilha casa por nome OU AUMID, mas disparar exige o AUMID exato,
# entao um nome curto e resolvido contra quem ja notificou nesta sessao.
$aumid = $App
if ($App -notmatch '[\\.]') {
    $achado = Get-Toasts | Where-Object {
        $_.AppInfo.DisplayInfo.DisplayName -like "*$App*" -or $_.AppInfo.AppUserModelId -like "*$App*"
    } | Select-Object -First 1

    if (-not $achado) {
        throw "Nao achei um AUMID para '$App'. Rode com -Listar depois de receber uma notificacao desse app."
    }
    $aumid = $achado.AppInfo.AppUserModelId
}

if (-not (Get-Process DynamicIsland -ErrorAction SilentlyContinue)) {
    Write-Host "A ilha nao esta rodando. Abra antes, senao nao ha o que popar." -ForegroundColor Yellow
    return
}

Write-Host "Disparando $Vezes toast(s) como '$aumid'." -ForegroundColor Cyan
Write-Host "Olhe para a ilha: ela deve expandir em cada um."
Write-Host ""

foreach ($i in 1..$Vezes) {
    $xml = [Windows.Data.Xml.Dom.XmlDocument]::new()
    $xml.LoadXml("<toast><visual><binding template='ToastGeneric'><text>Teste $i de $Vezes</text><text>Se a ilha expandir agora, o caminho esta funcionando.</text></binding></visual></toast>")

    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($aumid).Show(
        [Windows.UI.Notifications.ToastNotification]::new($xml))

    Write-Host ("  toast {0} disparado" -f $i)
    if ($i -lt $Vezes) { Start-Sleep -Seconds 6 }
}

Write-Host ""
Write-Host "Popou em todos? Entao mensagem repetida na mesma conversa tambem vai aparecer."
Write-Host "Popou so no primeiro? Me avise: e o caso de toast substituido."
