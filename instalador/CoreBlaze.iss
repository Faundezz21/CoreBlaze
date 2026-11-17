; instalador de coreblaze, se compila con inno setup 6 usando instalador\compilar.ps1

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppName "CoreBlaze"
#define AppPublisher "Faundezz21"
#define AppURL "https://github.com/Faundezz21/CoreBlaze"
#define AppExe "CoreBlaze.exe"

[Setup]
AppId={{8D3C2B9E-4F1A-4C6B-9E2D-7A5B1C0F3E21}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases
AppCopyright=© 2026 {#AppPublisher}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\ejecutable
OutputBaseFilename={#AppName}-{#AppVersion}-setup
SetupIconFile=..\recursos\logo.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0
CloseApplications=yes

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
spanish.FaltaWebView2=CoreBlaze necesita Microsoft Edge WebView2 Runtime para mostrar su interfaz y no se encontró en este equipo.%n%nPuedes descargarlo gratis desde:%nhttps://developer.microsoft.com/microsoft-edge/webview2/%n%n¿Continuar con la instalación de todas formas?
english.FaltaWebView2=CoreBlaze needs the Microsoft Edge WebView2 Runtime to show its interface and it was not found on this computer.%n%nYou can download it for free from:%nhttps://developer.microsoft.com/microsoft-edge/webview2/%n%nContinue with the installation anyway?

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "..\ejecutable\app\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent shellexec

[Code]
const
  ClaveWebView2 = 'Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';

// revisa si el runtime de webview2 esta instalado para el equipo o el usuario
function TieneWebView2(): Boolean;
var
  Version: String;
begin
  Result :=
    (RegQueryStringValue(HKLM, 'SOFTWARE\WOW6432Node\' + ClaveWebView2, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKLM, 'SOFTWARE\' + ClaveWebView2, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'Software\' + ClaveWebView2, 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not TieneWebView2() then
    Result := MsgBox(CustomMessage('FaltaWebView2'), mbConfirmation, MB_YESNO) = IDYES;
end;
