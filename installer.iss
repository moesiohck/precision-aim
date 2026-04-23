; ────────────────────────────────────────────────────────────────────
; Precision Aim Assist — Inno Setup Installer Script
; ────────────────────────────────────────────────────────────────────

#define MyAppName "Precision Aim Assist"
#define MyAppVersion "1.0"
#define MyAppPublisher "Precision Software"
#define MyAppExeName "AimAssistPro.exe"
#define MyAppIcon "C:\Users\HCK PC\Downloads\aim assist\AimAssistPro\Resources\app copy.ico"

[Setup]
AppId={{B7F42A53-9D41-4A6E-B123-PRECISIONAIM01}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf64}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Saída do instalador
OutputDir=C:\Users\HCK PC\Desktop
OutputBaseFilename=PrecisionAimAssist_Setup
; Ícone do instalador
SetupIconFile={#MyAppIcon}
; Compressão LZMA2 ultra (melhor razão de compressão)
Compression=lzma2/ultra64
SolidCompression=yes
; Visual
WizardStyle=modern
; Requer admin (necessário para ViGEm/Interception)
PrivilegesRequired=admin
; Desinstalar
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "portuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Area de Trabalho"
Name: "startmenu"; Description: "Criar atalho no Menu Iniciar"

[Files]
; EXE principal (single-file, self-contained)
Source: "C:\Users\HCK PC\Desktop\AimAssist_FINAL\AimAssistPro.exe"; DestDir: "{app}"; Flags: ignoreversion
; WebView2 Loader
Source: "C:\Users\HCK PC\Desktop\AimAssist_FINAL\WebView2Loader.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; Interception DLL (necessária para o driver)
Source: "C:\Users\HCK PC\Desktop\AimAssist_FINAL\interception.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
; Outros arquivos de suporte
Source: "C:\Users\HCK PC\Desktop\AimAssist_FINAL\*.dll"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "C:\Users\HCK PC\Desktop\AimAssist_FINAL\*.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

[Icons]
; Atalho na Área de Trabalho
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#MyAppExeName}"
; Atalho no Menu Iniciar
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenu

[Run]
; Executa após instalação
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName}"; Flags: shellexec nowait postinstall skipifsilent

[UninstallDelete]
; Limpa dados locais na desinstalação
Type: filesandordirs; Name: "{localappdata}\AimAssistPro"
Type: filesandordirs; Name: "{userappdata}\AimAssistPro"
