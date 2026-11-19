# Dynamic Island para Windows

Uma pílula no topo da tela que expande quando algo acontece. App WPF autônomo —
não precisa de Windhawk nem injeta código em processo nenhum do sistema.

## A pílula ociosa

```
18:38  qui., 12 nov. │ 🌧 22° │ ⚡ 100%  ● ●
```

Relógio, data, tempo agora, bateria e dois pontos de privacidade: **verde**
quando algum app está usando a câmera, **laranja** quando está usando o
microfone. A largura acompanha o conteúdo — sem clima nem bateria ela encolhe.

Um ponto verde à esquerda do relógio indica que tem mídia tocando.

## Expansões automáticas

| Situação | O que aparece |
|---|---|
| Tocando música | Capa, título, artista, progresso e botões anterior/play/próxima |
| Volume mudou | Barra de volume (ou ícone de mudo) |
| Brilho mudou | Barra de brilho — só em notebooks, via WMI |
| Carregador conectado/removido | Porcentagem da bateria |
| Bateria ≤ 20% | Aviso, uma vez por descarga |

Passar o mouse por cima abre o painel de mídia.

## Painéis

Clique na pílula para abrir. As abas ficam no topo; o × fecha.

- **Calendário** — grade mensal que ajusta a altura ao número de semanas do
  mês. Hoje em destaque na cor de realce do Windows, ponto embaixo dos dias com
  compromisso, e os eventos do dia selecionado listados abaixo.
- **Tempo** — temperatura, condição, sensação, umidade e vento, via wttr.in.
- **Hardware** — CPU, GPU, RAM, disco e rede. As barras ficam âmbar acima de
  75% e vermelhas acima de 90%.
- **Volume** — clique ou arraste a barra para ajustar o volume geral; clique no
  alto-falante para silenciar.

Enquanto um painel está aberto, mudanças de volume e brilho não o interrompem.

No canto superior direito, uma bolinha por aba ativa mostra em qual você está.

## Configurações

Clique no ícone da Dynamic Island na bandeja do sistema (ou botão direito →
**Configurações**). Tudo vale na hora, sem reiniciar:

- Ligar ou desligar clima, bateria e os pontos de câmera/microfone na pílula.
- Ligar ou desligar cada expansão automática: música, volume, brilho, bateria.
- Ligar ou desligar cada aba do painel — as bolinhas acompanham.
- Cidade do clima.
- Conta Google para o calendário (abaixo).

## Mover

Arraste a pílula para onde quiser, inclusive para um segundo monitor. A posição
é salva e restaurada no próximo boot. Clique sem arrastar continua abrindo o
painel — o arraste só começa depois de 5 px de movimento.

## Conectar seu calendário

Os eventos vêm de duas fontes, somadas e sem duplicar: a conta Google conectada
nas Configurações e o que o Windows já sincroniza.

### Google Agenda

O app não traz credenciais próprias — você cria as suas, uma vez:

1. No [Google Cloud Console](https://console.cloud.google.com/apis/credentials),
   crie um projeto, ative a **Google Calendar API** e crie um **ID do cliente
   OAuth** do tipo **App para computador**.
2. Em Tela de permissão OAuth, publique o app em **Em produção**. Em "Teste" o
   Google invalida o login a cada 7 dias.
3. Cole o ID e o segredo em Configurações → Google Agenda e clique em
   **Entrar com o Google**. O login abre no navegador padrão. Na tela de
   permissões, **deixe marcada a caixa "Ver seus calendários"** — o Google
   deixa concluir o login sem ela, mas aí nenhum evento aparece. O painel do
   calendário mostra em vermelho o motivo quando o Google recusa a leitura.

Só leitura (`calendar.readonly`), de todos os calendários marcados como
visíveis na sua conta. O token de acesso fica em `%APPDATA%\DynamicIsland\google.token`,
criptografado para o seu usuário do Windows; o ID e o segredo ficam no
`settings.json`. **Sair da conta Google** revoga o acesso.

### Calendário do Windows

Para popular:

1. Abra o app **Calendário** (ou **Outlook**) do Windows.
2. Adicione sua conta Google, Outlook ou iCloud.
3. Espere sincronizar e reabra o painel.

Sem nenhuma conta adicionada o painel mostra o grid e a mensagem "Nenhuma conta
no Calendário do Windows". Eventos recorrentes vêm já expandidos pelo Windows,
então não há interpretação de RRULE para dar errado.

## Baixar em outra máquina

Precisa do **.NET 9 SDK** instalado. Depois:

```bash
gh repo clone Arthur-Rabello/dynamic-island-windows
```

```powershell
cd dynamic-island-windows; dotnet publish .\DynamicIsland\DynamicIsland.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\publish
```

E ative o início automático naquela máquina também:

```powershell
.\inicio-automatico.ps1
```

O repositório é privado, então a máquina precisa estar autenticada — `gh auth
login` resolve, e `gh auth setup-git` deixa `git pull` e `git push`
funcionando sem pedir senha.

## Atualizar

Numa máquina que já tem o clone, para pegar as mudanças feitas na outra:

```powershell
git pull; dotnet publish .\DynamicIsland\DynamicIsland.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\publish
```

Feche a ilha pela bandeja antes de republicar, senão o build falha dizendo que
o `.exe` está em uso por outro processo.

Para enviar mudanças suas:

```bash
git add -A; git commit -m "o que mudou"; git push
```

A configuração em `%APPDATA%` não é versionada, então cada máquina mantém a
própria posição da ilha e a própria cidade do clima.

## Rodar

```powershell
dotnet run --project .\DynamicIsland
```

Ou o executável publicado, em `publish\DynamicIsland.exe`.

Para republicar depois de mexer no código:

```powershell
dotnet publish .\DynamicIsland\DynamicIsland.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\publish
```

Flags de inspeção de layout, úteis pra ajustar o visual sem esperar o estado
acontecer:

```powershell
.\publish\DynamicIsland.exe --demo
```

```powershell
.\publish\DynamicIsland.exe --panel=calendar
```

`--panel` aceita `calendar`, `weather`, `hardware` ou `volume`. `--event`
mostra na pílula um evento de mentira daqui a 12 minutos. `--settings` abre a janela
de configurações já na inicialização.

Para fechar: ícone na bandeja do sistema → botão direito → Sair.

## Início automático

```powershell
.\inicio-automatico.ps1
```

Cria um atalho na pasta Inicializar do seu usuário. `-Remover` desfaz.

## Configuração

`%APPDATA%\DynamicIsland\settings.json`, criado no primeiro arraste:

```json
{
  "CenterX": 960,
  "Top": 6,
  "WeatherLocation": "",
  "LastPanel": "Calendar"
}
```

`WeatherLocation` vazio deixa o wttr.in resolver a cidade pelo IP da
requisição. Preencha com `"Sao Paulo"` para fixar — e aí nem o IP é usado para
localização.

## Consumo medido

Ocioso, nesta máquina (8 núcleos), com a pílula mostrando relógio, clima e
bateria:

- 163 MB de working set, 75 MB privados — típico de WPF
- 0,08% de CPU

Com o painel de hardware aberto o custo sobe, porque é o único momento em que
os contadores de desempenho rodam. Feche o painel e volta ao número acima.

## Como funciona

- **Mídia** — `GlobalSystemMediaTransportControlsSessionManager` (WinRT). Pega
  qualquer app que registre sessão de mídia: Spotify, aba do navegador, player
  local.
- **Calendário** — `AppointmentManager.RequestStoreAsync` com
  `AllCalendarsReadOnly`. Funciona em processo não empacotado, sem identidade
  de pacote.
- **Volume** — COM `IAudioEndpointVolume`, lido a cada 150 ms. Poll em vez de
  callback: duas leituras de propriedade não custam nada medível e o endpoint é
  readquirido de tempos em tempos, então trocar de saída de áudio funciona sem
  bookkeeping extra.
- **Brilho** — evento WMI `WmiMonitorBrightnessEvent`. Em desktop a classe não
  existe e o recurso fica dormente.
- **Bateria** — `GetSystemPowerStatus`.
- **Câmera e microfone** — `ConsentStore` no registro do usuário. Cada app que
  já usou o dispositivo tem uma subchave com `LastUsedTimeStart` e
  `LastUsedTimeStop`; `Stop` igual a zero significa sessão aberta. É a mesma
  fonte que o indicador de privacidade da bandeja usa.
- **Hardware** — `GetSystemTimes` (CPU), `GlobalMemoryStatusEx` (RAM),
  contadores de desempenho `GPU Engine` e `PhysicalDisk`, e deltas de
  `NetworkInterface` (rede). Só roda enquanto o painel está aberto.
- **Janela** — `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW`: clicar nela nunca rouba
  o foco do que você está digitando, e ela não aparece no Alt+Tab nem na barra
  de tarefas.

Duas decisões de layout que valem conhecer antes de mexer:

- As barras de progresso são duas colunas `*` de um `Grid`, não larguras em
  pixels — assim ficam corretas enquanto a janela ainda está animando de um
  tamanho pro outro.
- O arraste só captura o mouse depois de passar do limiar. Capturar no
  `MouseDown` roubaria os cliques dos botões e das células do calendário.

## Ajustes rápidos

Constantes no topo de `DynamicIsland/MainWindow.xaml.cs`:

- `CollapsedWidth` / `MediaWidth` / `OsdWidth` / `PanelWidth`
- `DefaultTop`, `DragThreshold`
- `OsdDwell` / `MediaDwell`

Cores e estilos compartilhados ficam em `App.xaml`.

## Limitações conhecidas

- Brilho depende do painel expor `WmiMonitorBrightnessEvent`; alguns monitores
  externos não expõem.
- O volume é lido por polling, então uma mudança muito rápida pode ser agrupada
  numa leitura só.
- Os ícones de tempo usam Segoe UI Emoji. Nem todo código existe na fonte —
  `U+1F32B` (neblina), por exemplo, cai de volta para a nuvem simples.
- O painel de hardware soma as instâncias `engtype_3D` para a GPU, que é o que
  o Gerenciador de Tarefas mostra como número principal. Cargas de computação
  pura podem não aparecer.
