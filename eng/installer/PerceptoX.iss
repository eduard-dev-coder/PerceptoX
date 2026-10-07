#ifndef PackageDir
  #error PackageDir must point to a verified standalone folder
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef TermsFile
  #error TermsFile is required
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{EDFF7E2D-24BA-4B25-B20C-981B7623C438}
AppName=PerceptoX
AppVersion={#AppVersion}
AppPublisher=Prepelita Eduard
AppSupportURL=mailto:eduard.condact.dev@gmail.com
DefaultDirName={localappdata}\Programs\PerceptoX
DefaultGroupName=PerceptoX
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern
SetupIconFile={#PackageDir}\app\graphics\PerceptoX.ico
UninstallDisplayIcon={app}\PerceptoX.exe
OutputDir={#OutputDir}
OutputBaseFilename=PerceptoX-{#AppVersion}-Setup-win-x64
Compression=lzma2
SolidCompression=yes
LicenseFile={#PackageDir}\licenses\LICENSE
CreateUninstallRegKey=not IsValidationMode
CloseApplications=yes
RestartApplications=no
ChangesEnvironment=no
DisableProgramGroupPage=no
AllowNoIcons=yes
UsePreviousTasks=yes

[Languages]
Name: "ro"; MessagesFile: "compiler:Languages\Romanian-PerceptoX.isl,Messages.ro.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ro.RuntimeComponent=Aplicație și runtime-uri locale .NET / WinUI / SQLite
ro.CodecsComponent=Codecuri offline HEIC / HEIF / AVIF (activare app-local)
ro.DesktopIcon=Scurtătură pe Desktop
ro.Shortcuts=Scurtături:
ro.TermsTitle=Licențe și notificări ale componentelor terțe
ro.TermsDescription=Consultați condițiile aplicabile dependențelor incluse. Nu sunt descărcate componente în timpul instalării.
ro.AcceptTerms=Am citit și accept condițiile aplicabile componentelor terțe selectate.
ro.ConsentRequired=Instalarea silent necesită acord explicit: /ACCEPTLICENSES=YES. Folosiți wizard-ul pentru a consulta licențele.
ro.CodecFailure=Activarea codecurilor offline a eșuat. Consultați app\modules\offline-install-error.txt și folosiți Module pentru reparare. Aplicația poate procesa în continuare formatele native.
ro.LaunchApp=Pornește PerceptoX
en.RuntimeComponent=Application and app-local .NET / WinUI / SQLite runtimes
en.CodecsComponent=Offline HEIC / HEIF / AVIF codecs (app-local activation)
en.DesktopIcon=Desktop shortcut
en.Shortcuts=Shortcuts:
en.TermsTitle=Third-party licenses and notices
en.TermsDescription=Review the terms applicable to bundled dependencies. No components are downloaded during installation.
en.AcceptTerms=I have read and accept the applicable terms for selected third-party components.
en.ConsentRequired=Silent installation requires explicit consent: /ACCEPTLICENSES=YES. Use the wizard to review licenses.
en.CodecFailure=Offline codec activation failed. Check app\modules\offline-install-error.txt and use Modules to repair. Native image formats remain available.
en.LaunchApp=Launch PerceptoX

[Types]
Name: "full"; Description: "PerceptoX + offline HEIC / AVIF"
Name: "custom"; Description: "Custom"; Flags: iscustom

[Components]
Name: "app"; Description: "{cm:RuntimeComponent}"; Types: full custom; Flags: fixed
Name: "codecs"; Description: "{cm:CodecsComponent}"; Types: full custom; Flags: fixed

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Shortcuts}"; Check: not IsValidationMode

[Files]
Source: "{#PackageDir}\PerceptoX.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\app\*"; DestDir: "{app}\app"; Excludes: "codecs\*"; Components: app; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageDir}\app\codecs\*"; DestDir: "{app}\app\codecs"; Components: codecs; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "compiler:license.txt"; DestDir: "{app}\licenses\InnoSetup"; Flags: ignoreversion
Source: "{#PackageDir}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PackageDir}\package-manifest.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#TermsFile}"; DestDir: "{tmp}"; Flags: dontcopy

[Icons]
Name: "{group}\PerceptoX"; Filename: "{app}\PerceptoX.exe"; WorkingDir: "{app}"; Check: not IsValidationMode
Name: "{group}\Uninstall PerceptoX"; Filename: "{uninstallexe}"; Check: not IsValidationMode
Name: "{autodesktop}\PerceptoX"; Filename: "{app}\PerceptoX.exe"; WorkingDir: "{app}"; Tasks: desktopicon; Check: not IsValidationMode

[Run]
Filename: "{app}\PerceptoX.exe"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent; Check: not IsValidationMode

[Code]
var
  TermsPage: TWizardPage;
  TermsMemo: TNewMemo;
  AcceptTerms: TNewCheckBox;

function IsValidationMode: Boolean;
begin
  Result := CompareText(ExpandConstant('{param:VALIDATIONMODE|NO}'), 'YES') = 0;
end;

function ExplicitSilentConsent: Boolean;
begin
  Result := CompareText(ExpandConstant('{param:ACCEPTLICENSES|NO}'), 'YES') = 0;
end;

function InitializeSetup: Boolean;
begin
  Result := (not WizardSilent) or ExplicitSilentConsent;
  if not Result then Log('Refused silent installation without explicit license consent.');
end;

procedure AcceptTermsChanged(Sender: TObject);
begin
  if WizardForm.CurPageID = TermsPage.ID then WizardForm.NextButton.Enabled := AcceptTerms.Checked;
end;

procedure InitializeWizard;
var
  Text: AnsiString;
begin
  TermsPage := CreateCustomPage(wpLicense, CustomMessage('TermsTitle'), CustomMessage('TermsDescription'));
  TermsMemo := TNewMemo.Create(TermsPage);
  TermsMemo.Parent := TermsPage.Surface;
  TermsMemo.SetBounds(0, 0, TermsPage.SurfaceWidth, TermsPage.SurfaceHeight - ScaleY(55));
  TermsMemo.ReadOnly := True;
  TermsMemo.ScrollBars := ssVertical;
  TermsMemo.WordWrap := True;
  ExtractTemporaryFile('ThirdParty-terms.txt');
  if not LoadStringFromFile(ExpandConstant('{tmp}\ThirdParty-terms.txt'), Text) then RaiseException('License bundle missing.');
  TermsMemo.Text := UTF8Decode(Text);
  AcceptTerms := TNewCheckBox.Create(TermsPage);
  AcceptTerms.Parent := TermsPage.Surface;
  AcceptTerms.SetBounds(0, TermsMemo.Height + ScaleY(10), TermsPage.SurfaceWidth, ScaleY(40));
  AcceptTerms.Caption := CustomMessage('AcceptTerms');
  AcceptTerms.Checked := WizardSilent and ExplicitSilentConsent;
  AcceptTerms.OnClick := @AcceptTermsChanged;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = TermsPage.ID then WizardForm.NextButton.Enabled := AcceptTerms.Checked;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := (CurPageID <> TermsPage.ID) or AcceptTerms.Checked;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if WizardSilent and not ExplicitSilentConsent then Result := CustomMessage('ConsentRequired');
  if (not WizardSilent) and (not AcceptTerms.Checked) then Result := CustomMessage('ConsentRequired');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ExitCode: Integer;
begin
  if (CurStep = ssPostInstall) and WizardIsComponentSelected('codecs') then begin
    if (not Exec(ExpandConstant('{app}\app\PerceptoX.WinUI.exe'), '--install-offline-codecs --accept-codec-licenses', ExpandConstant('{app}\app'), SW_HIDE, ewWaitUntilTerminated, ExitCode)) or (ExitCode <> 0) then
      RaiseException(CustomMessage('CodecFailure'));
  end;
end;
