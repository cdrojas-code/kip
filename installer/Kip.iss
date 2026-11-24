; Instalador de Kip (Inno Setup 6).
; Lo compila build.ps1 tras publicar la app self-contained en artifacts\publish.
; Instalación por usuario (sin permisos de administrador); el diálogo permite elegir "para todos" si se tiene admin.

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#define AppName "Kip"
#define AppExe "Kip.exe"
#define AppAuthor "Carlos Daniel Rojas"
#define PublishDir "..\artifacts\publish"

[Setup]
AppId={{7C4D2E51-9B3A-4F0E-8D61-2A5F3C9B7E10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppAuthor}
AppCopyright=© {#AppAuthor}
AppComments=Notas adhesivas siempre a la vista. Creado por {#AppAuthor} ("cdrojas") con la ayuda de Claude.
VersionInfoCompany={#AppAuthor}
VersionInfoDescription=Instalador de {#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 1809 (LTSC 2019) o superior
MinVersion=10.0.17763
OutputDir=..\artifacts\installer
OutputBaseFilename=Kip-Setup-{#AppVersion}
SetupIconFile=..\src\StickyNotes.App\Assets\Kip.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Cierra la app si está abierta durante la instalación/actualización
AppMutex=Local\Kip.SingleInstance
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
; El inicio con Windows lo gestiona la app (activado por defecto; se cambia en el menú de la bandeja).
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; GroupDescription: "Opciones:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Mismo valor que escribe AutostartService (HKCU Run "Kip")
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Kip"; ValueData: """{app}\{#AppExe}"" --autostart"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir Kip ahora"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillApp"
; Quita el registro de notificaciones de Windows y el autoarranque (no toca las notas)
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall-cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"

[UninstallDelete]
; Las notas del usuario (%LOCALAPPDATA%\Kip) NO se borran al desinstalar, a propósito.
Type: filesandordirs; Name: "{app}"
