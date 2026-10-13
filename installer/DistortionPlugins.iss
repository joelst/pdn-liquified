#ifndef PackageDirectory
  #error PackageDirectory must point to the flat Release package.
#endif
#ifndef AppVersion
  #error AppVersion is required.
#endif
#ifndef OutputDirectory
  #error OutputDirectory is required.
#endif
#ifndef NumericVersion
  #error NumericVersion is required.
#endif

[Setup]
AppId={{E87EFCDA-0785-41CC-95A7-237057E641A6}
AppName=Paint.NET Distortion Plugins
AppVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}
VersionInfoProductVersion={#NumericVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
AppPublisher=pdn-liquified contributors
AppPublisherURL=https://github.com/joelst/pdn-liquified
AppSupportURL=https://github.com/joelst/pdn-liquified/issues
DefaultDirName={userdocs}\paint.net App Files\Effects
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible arm64
MinVersion=10.0.19044
WizardStyle=modern
DisableWelcomePage=no
DirExistsWarning=no
UsePreviousAppDir=yes
UninstallFilesDir={app}\pdn-liquified
UninstallDisplayName=Paint.NET Distortion Plugins
LicenseFile={#PackageDirectory}\LICENSE
OutputDir={#OutputDirectory}
OutputBaseFilename=pdn-liquified-setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
#ifdef SignedBuild
SignTool=artifactsigning
SignedUninstaller=yes
#endif

[Dirs]
Name: "{app}\pdn-liquified"; Flags: uninsalwaysuninstall

[Files]
Source: "{#PackageDirectory}\LiquifiedPlugin.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\GridWarpPlugin.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\DocumentRectifyPlugin.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\LICENSE"; DestDir: "{app}\pdn-liquified"; Flags: ignoreversion
Source: "{#PackageDirectory}\README.md"; DestDir: "{app}\pdn-liquified"; Flags: ignoreversion
Source: "{#PackageDirectory}\INSTALL.md"; DestDir: "{app}\pdn-liquified"; Flags: ignoreversion

[Messages]
WelcomeLabel2=This installs Liquified, Grid Warpified, and Doc Recitified for Paint.NET 5.1.12 or later.%n%nClose Paint.NET before continuing. The default destination is your Documents Effects folder and does not require administrator access.%n%nFor a portable installation, select its Effects folder. Do not install duplicate copies in both the user and application Effects folders.
SelectDirLabel3=Select the Paint.NET Effects folder. The default supports classic and Microsoft Store Paint.NET.

[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  SupportDirectory: String;
  ProbePath: String;
begin
  Result := '';
  SupportDirectory := ExpandConstant('{app}\pdn-liquified');
  if not ForceDirectories(SupportDirectory) then
  begin
    Result := 'Cannot create the plugin support folder:' + #13#10 +
      SupportDirectory + #13#10 + #13#10 +
      'Check that the destination is available and writable. If Documents is ' +
      'redirected to OneDrive, start OneDrive and make the folder available ' +
      'on this device, then retry. For portable Paint.NET, select its writable ' +
      'Effects folder. Setup will not install to a different folder automatically.';
    Log(Result);
    Exit;
  end;
  ProbePath := SupportDirectory + '\setup-write-check-' +
    GetDateTimeString('yyyymmddhhnnss', '-', ':') + '.tmp';
  if FileExists(ProbePath) then
  begin
    Result := 'A setup write-check file already exists. Retry installation.';
    Log(Result);
    Exit;
  end;
  if not SaveStringToFile(ProbePath, 'Paint.NET Distortion Plugins setup write check', False) then
  begin
    Result := 'Setup cannot write to the plugin support folder:' + #13#10 +
      SupportDirectory + #13#10 + #13#10 +
      'Check Windows Security > Virus & threat protection > Protection history. ' +
      'Controlled Folder Access can block the temporary pdn-liquified-setup.tmp ' +
      'process even when the folder is writable in Explorer. If the signed ' +
      'installer is trusted, use Windows Security to review and allow the blocked ' +
      'app, then retry. Do not disable antivirus protection. Also check that ' +
      'OneDrive files are available locally.';
    Log(Result);
    Exit;
  end;
  if not DeleteFile(ProbePath) then
  begin
    Result := 'Setup cannot remove its write-check file: ' + ProbePath + #13#10 +
      'Check destination permissions and Windows Security Protection history, then retry.';
    Log(Result);
  end;
end;
