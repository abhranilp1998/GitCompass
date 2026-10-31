; GitCompass per-user installer (Inno Setup 6). Compile with:
;   ISCC.exe /DAppVersion=0.2.8 /DSourceDir=<published folder> /DOutputDir=<artifacts> installer\GitCompass.iss
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #error SourceDir must point at the published GitCompass folder
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId={{6F3B2C8E-4A1D-4E7B-9C25-8D0F1A7E3B64}
AppName=GitCompass
AppVersion={#AppVersion}
AppVerName=GitCompass {#AppVersion}
AppPublisher=Abhranil Paul
AppPublisherURL=https://github.com/abhranilp1998/GitCompass
AppSupportURL=https://github.com/abhranilp1998/GitCompass/issues
AppUpdatesURL=https://github.com/abhranilp1998/GitCompass/releases
DefaultDirName={localappdata}\Programs\GitCompass
DefaultGroupName=GitCompass
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
SetupIconFile=..\assets\branding\GitCompass.ico
UninstallDisplayIcon={app}\GitCompass.exe
UninstallDisplayName=GitCompass
OutputDir={#OutputDir}
OutputBaseFilename=GitCompass-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install GitCompass {#AppVersion} on your computer.%n%nGitCompass helps you use Git and GitHub step by step, even if you have never used them before. You do not need administrator rights.%n%nClick Next to continue.
LicenseLabel3=GitCompass is free and open source under the MIT license. In short: you can use it for anything. Please read and accept to continue.

[Tasks]
Name: "desktopicon"; Description: "Put a GitCompass shortcut on my desktop"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\GitCompass"; Filename: "{app}\GitCompass.exe"
Name: "{autodesktop}\GitCompass"; Filename: "{app}\GitCompass.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\GitCompass.exe"; Description: "Launch GitCompass"; Flags: nowait postinstall skipifsilent
