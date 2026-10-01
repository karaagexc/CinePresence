#ifndef AppVersion
  #define AppVersion "0.2.2"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\CinePresence-" + AppVersion + "-win-x64"
#endif
[Setup]
AppId={{D9D6334A-9F29-48DD-916F-87A6FEC58570}
AppName=CinePresence
AppVersion={#AppVersion}
AppPublisher=CinePresence contributors
AppPublisherURL=https://github.com/karaagexc/CinePresence
DefaultDirName={localappdata}\Programs\CinePresence
DefaultGroupName=CinePresence
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts
OutputBaseFilename=CinePresence-{#AppVersion}-Setup-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\CinePresence.exe
SetupIconFile=..\src\CinePresence.App\Assets\CinePresence.ico
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: companion; Description: "Prepare the optional Chrome/Edge companion (browser approval required)"; GroupDescription: "Browser detection:"
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\CinePresence"; Filename: "{app}\CinePresence.exe"
Name: "{group}\Browser companion setup"; Filename: "{app}\browser-setup.html"; Tasks: companion
Name: "{userdesktop}\CinePresence"; Filename: "{app}\CinePresence.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\browser-setup.html"; Description: "Enable the browser companion (guided setup)"; Flags: shellexec postinstall skipifsilent; Tasks: companion
Filename: "{app}\CinePresence.exe"; Description: "Open CinePresence"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{app}\browser-host.json"

[InstallDelete]
Type: files; Name: "{app}\browser-companion\metadata.js"
Type: files; Name: "{app}\browser-companion\content.js"
Type: files; Name: "{app}\browser-companion\worker.js"
Type: files; Name: "{app}\browser-companion\popup.js"

[Code]
var
  SavedChromeHost, SavedEdgeHost: String;

function HostKey(Browser: String): String;
begin
  Result := 'Software\' + Browser + '\NativeMessagingHosts\org.cinepresence.companion';
end;

function SuspendOwnedHost(Browser: String; var Saved: String): Boolean;
var Key, Value: String;
begin
  Result := True;
  if Saved <> '' then exit;
  Key := HostKey(Browser);
  if RegQueryStringValue(HKCU, Key, '', Value) and
    (CompareText(Value, ExpandConstant('{app}\browser-host.json')) = 0) then begin
    Saved := Value;
    Result := RegDeleteValue(HKCU, Key, '');
    if not Result then Saved := '';
    if Result then Log('Suspended own browser connection during file replacement: ' + Browser);
  end;
end;

procedure RestoreOwnedHost(Browser, Saved: String);
begin
  if (Saved <> '') and FileExists(Saved) and not RegValueExists(HKCU, HostKey(Browser), '') then
    RegWriteStringValue(HKCU, HostKey(Browser), '', Saved);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  // Prevent the extension from respawning a helper that Restart Manager just
  // closed. Only suspend this installation's registrations, never another copy.
  if not SuspendOwnedHost('Google\Chrome', SavedChromeHost) or
     not SuspendOwnedHost('Microsoft\Edge', SavedEdgeHost) then
    Result := 'Close CinePresence Companion connections and retry the update.';
end;

procedure DeinitializeSetup;
begin
  // Also restore after cancellation/rollback; browser settings are not changed.
  RestoreOwnedHost('Google\Chrome', SavedChromeHost);
  RestoreOwnedHost('Microsoft\Edge', SavedEdgeHost);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('companion') then
    if not Exec(ExpandConstant('{app}\CinePresence.exe'), '--register-browser', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
      MsgBox('The app was installed. Open CinePresence Settings and choose Set up companion to complete the browser connection.', mbInformation, MB_OK);
end;

procedure RemoveOwnedHost(Browser: String);
var Key, Value: String;
begin
  Key := 'Software\' + Browser + '\NativeMessagingHosts\org.cinepresence.companion';
  if RegQueryStringValue(HKCU, Key, '', Value) and (CompareText(Value, ExpandConstant('{app}\browser-host.json')) = 0) then
    RegDeleteKeyIncludingSubkeys(HKCU, Key);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Value: String;
begin
  if CurUninstallStep = usUninstall then begin
    RemoveOwnedHost('Google\Chrome'); RemoveOwnedHost('Microsoft\Edge');
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CinePresence', Value) and
      (Pos(Lowercase(ExpandConstant('{app}\CinePresence.exe')), Lowercase(Value)) > 0) then
      RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CinePresence');
  end;
end;
