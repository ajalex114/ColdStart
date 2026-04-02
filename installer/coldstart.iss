; ColdStart Installer Script for Inno Setup 6+
; https://jrsoftware.org/isinfo.php

#define MyAppName "ColdStart"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ajalex114"
#define MyAppURL "https://github.com/ajalex114/ColdStart"
#define MyAppExeName "ColdStart.exe"

[Setup]
AppId={{B8F3A2C1-7D4E-4F5A-9B6C-1E2D3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=
OutputDir=..\dist
OutputBaseFilename=ColdStartSetup-{#MyAppVersion}
; SetupIconFile=..\src\ColdStart\Assets\coldstart.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1; Check: not IsAdminInstallMode

[Files]
Source: "..\src\ColdStart\bin\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\src\ColdStart\bin\publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion; Check: not IsSingleFile
Source: "..\src\ColdStart\bin\publish\*.json"; DestDir: "{app}"; Flags: ignoreversion; Check: not IsSingleFile

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Run the app with --uninstall flag to reverse all changes and clean up
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated skipifdoesntexist

[UninstallDelete]
; Remove app data directory
Type: filesandordirs; Name: "{localappdata}\ColdStart"

[Code]
function IsSingleFile: Boolean;
begin
  Result := not FileExists(ExpandConstant('{app}\ColdStart.deps.json'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Clean up any remaining app data
    DelTree(ExpandConstant('{localappdata}\ColdStart'), True, True, True);
  end;
end;
