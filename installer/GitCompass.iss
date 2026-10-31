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
Filename: "https://git-scm.com/downloads/win"; Description: "Open the Git download page (needed first)"; Flags: postinstall shellexec nowait skipifsilent; Check: GitMissing
Filename: "{app}\GitCompass.exe"; Description: "Launch GitCompass"; Flags: nowait postinstall skipifsilent

[Code]
// H10: GitCompass needs Git for Windows. Detect it and offer the download before finishing.
var
  GitPage: TOutputMsgWizardPage;
  GitFound: Boolean;

function GitInstalled(): Boolean;
var P: String;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\GitForWindows', 'InstallPath', P) or
    RegQueryStringValue(HKCU, 'SOFTWARE\GitForWindows', 'InstallPath', P) or
    FileExists(ExpandConstant('{commonpf64}\Git\cmd\git.exe')) or
    FileExists(ExpandConstant('{commonpf32}\Git\cmd\git.exe')) or
    FileExists(ExpandConstant('{localappdata}\Programs\Git\cmd\git.exe'));
end;

procedure InitializeWizard();
begin
  GitFound := GitInstalled();
  GitPage := CreateOutputMsgPage(wpSelectTasks,
    'One more free program: Git',
    'GitCompass uses Git for Windows to keep saved versions of your files.',
    'Git for Windows was not found on this computer.' + #13#10#13#10 +
    'Click Next to finish installing GitCompass. On the last page, leave "Open the Git download page" ticked, ' +
    'install Git with its default options (about 2 minutes), then start GitCompass.' + #13#10#13#10 +
    'Download page: https://git-scm.com/downloads/win');
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = GitPage.ID) and GitFound;
end;

function GitMissing(): Boolean;
begin
  Result := not GitFound;
end;
