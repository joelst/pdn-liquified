#ifndef PackageDirectory
  #error PackageDirectory must point to the flat Release package.
#endif
#ifndef AppVersion
  #error AppVersion is required.
#endif
#ifndef OutputDirectory
  #error OutputDirectory is required.
#endif

[Setup]
AppId={{E87EFCDA-0785-41CC-95A7-237057E641A6}
AppName=Paint.NET Distortion Plugins
AppVersion={#AppVersion}
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

[Files]
Source: "{#PackageDirectory}\LiquifiedPlugin.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\GridWarpPlugin.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\DocumentRectifyPlugin.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDirectory}\LICENSE"; DestDir: "{app}\pdn-liquified"; Flags: ignoreversion
Source: "{#PackageDirectory}\README.md"; DestDir: "{app}\pdn-liquified"; Flags: ignoreversion
Source: "{#PackageDirectory}\INSTALL.md"; DestDir: "{app}\pdn-liquified"; Flags: ignoreversion

[Messages]
WelcomeLabel2=This installs Liquified, Grid Warp, and Document Rectify for Paint.NET 5.1.12 or later.%n%nClose Paint.NET before continuing. The default destination is your Documents Effects folder and does not require administrator access.%n%nFor a portable installation, select its Effects folder. Do not install duplicate copies in both the user and application Effects folders.
SelectDirLabel3=Select the Paint.NET Effects folder. The default supports classic and Microsoft Store Paint.NET.
