; Instalador da Dynamic Island.
;
; Compile pelo publicar.ps1, que passa a versao e garante que publish\ esta
; atualizado. Compilar na mao tambem funciona:
;
;   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DAppVersion=1.0.0 instalador\DynamicIsland.iss
;
; Instala em %LOCALAPPDATA% de proposito: sem UAC, o que deixa a atualizacao
; ser um duplo clique em vez de uma elevacao.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "Dynamic Island"
#define AppExe "DynamicIsland.exe"
#define AppPublisher "Arthur Rabello"

[Setup]
AppId={{8F3A6C21-5D94-4E0B-9A7C-1E2B4D6F8A30}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

DefaultDirName={localappdata}\Programs\DynamicIsland
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes

; Sem admin: instala so para o usuario atual.
PrivilegesRequired=lowest

; Fecha a ilha antes de substituir o executavel, em vez de falhar por arquivo
; em uso. Sem AppMutex de proposito: na atualizacao pelo app, o instalador
; sobe enquanto a ilha ainda esta saindo, e o mutex abortaria a instalacao.
CloseApplications=yes
RestartApplications=no

OutputDir=..\dist
OutputBaseFilename=DynamicIsland-{#AppVersion}-setup
SetupIconFile=..\DynamicIsland\Assets\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "iniciarcomwindows"; Description: "Iniciar junto com o Windows"; GroupDescription: "Ao ligar o computador:"
Name: "atalhoarea"; Description: "Criar atalho na area de trabalho"; Flags: unchecked

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: atalhoarea
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: iniciarcomwindows

[Run]
; Roda tambem no modo silencioso, que e o da atualizacao pelo app: a ilha volta sozinha.
Filename: "{app}\{#AppExe}"; Description: "Abrir a {#AppName} agora"; Flags: nowait postinstall

[UninstallDelete]
; A configuracao fica fora do diretorio do app; some junto na desinstalacao.
Type: filesandordirs; Name: "{userappdata}\DynamicIsland"
