; Instalador do Acertô (Inno Setup 6).
; Gerar: 1) dotnet publish src/Acerto.Windows -c Release -r win-x64 --self-contained -o publish
;        2) ISCC.exe installer\acerto.iss   → dist\Acerto-Setup-<versão>.exe
; Instala por usuário, sem pedir administrador. Os dados do usuário (%APPDATA%\Acerto) ficam fora da pasta
; do programa e são mantidos na desinstalação.

#define AppName "Acertô"
#define AppVersion "1.0.0"
#define AppExe "Acerto.exe"
; pasta com o resultado do dotnet publish; dá para trocar: ISCC /DPublishDir=..\outra-pasta
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{6F0B2B7E-3C55-4E0B-9C0A-ACE27D0A0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Acertô
DefaultDirName={localappdata}\Programs\Acerto
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Acerto-Setup-{#AppVersion}
SetupIconFile=..\src\Acerto.Windows\Assets\acerto.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
ShowLanguageDialog=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "startup"; Description: "Iniciar o Acertô junto com o Windows"; GroupDescription: "Opções:"
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Opções:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; mesmo valor que o interruptor "Iniciar com o Windows" do próprio app usa
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Acerto"; ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o Acertô agora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; o app fica na bandeja: fecha antes de apagar os arquivos
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "FecharAcerto"
