; Instalador do InsightFlow Desktop (Inno Setup). Use installer\build.ps1, que publica o executavel e chama o compilador.
; Arquivo so com caracteres ASCII: os textos do assistente vem da traducao do Inno Setup.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\installer\app"
#endif
#define AppExe "InsightFlow.Desktop.exe"

[Setup]
AppId={{3E2CB640-D713-4914-8089-09AA4765F1A7}
AppName=InsightFlow
AppVersion={#AppVersion}
AppVerName=InsightFlow {#AppVersion}
AppPublisher=InsightF-AI
AppPublisherURL=https://github.com/InsightF-AI
VersionInfoVersion={#AppVersion}
; Instalacao por usuario, sem administrador: o appsettings.json ao lado do executavel continua editavel.
PrivilegesRequired=lowest
DefaultDirName={autopf}\InsightFlow
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Mesmo nome de SingleInstance.DefaultName: com o aplicativo aberto, inclusive na bandeja, o assistente pede para encerra-lo.
AppMutex=InsightFlow.Desktop
SetupIconFile=..\src\InsightFlow.Desktop\Assets\insightflow.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=InsightFlow
OutputDir=..\artifacts\installer
OutputBaseFilename=InsightFlow-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
#ifdef Sign
; A ferramenta "insightflow" e definida por build.ps1 (/Sinsightflow=...) e assina o instalador e o desinstalador.
SignTool=insightflow
SignedUninstaller=yes
#endif

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
; A URL da API pode ter sido editada: uma atualizacao so troca o arquivo que ainda tem o padrao antigo (ShouldInstallSettings).
Source: "{#SourceDir}\appsettings.json"; DestDir: "{app}"; Check: ShouldInstallSettings

[Icons]
Name: "{autoprograms}\InsightFlow"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\InsightFlow"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,InsightFlow}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Preferencias locais (preferences.json); nao ha tokens nem senhas gravados.
Type: filesandordirs; Name: "{localappdata}\InsightFlow"

[Code]
function ShouldInstallSettings: Boolean;
var
  Path: String;
  Content: AnsiString;
begin
  Path := ExpandConstant('{app}\appsettings.json');
  // Os primeiros instaladores gravavam o servidor local como padrao; sem edicao, o arquivo passa a usar a API de producao.
  Result := not FileExists(Path) or
    (LoadStringFromFile(Path, Content) and (Pos('"http://localhost:8000"', String(Content)) > 0));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // A entrada "Abrir o InsightFlow ao iniciar o Windows" e criada pelo aplicativo, em Configuracoes.
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'InsightFlow');
end;
