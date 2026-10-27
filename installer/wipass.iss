; WiPass installer (Inno Setup 6)
; Per-user install: no administrator rights needed.
; Build: iscc /DMyAppVersion=0.3.0-alpha.1 /DMyAppVersionNumeric=0.3.0 installer\wipass.iss

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif
#ifndef MyAppVersionNumeric
  #define MyAppVersionNumeric "0.0.0"
#endif

#define MyAppName "와이패스 (WiPass)"
#define MyAppExe "WiPass.exe"
#define BinDir "..\src\WiPass\bin\Release\net48"

; Korean messages ship with recent Inno Setup versions; otherwise the build fetches Korean.isl next to this file.
#if FileExists(AddBackslash(CompilerPath) + "Languages\Korean.isl")
  #define KoreanMessages "compiler:Languages\Korean.isl"
#else
  #define KoreanMessages "Korean.isl"
#endif

[Setup]
AppId={{8B4E2A61-3C7D-4F0B-9E57-6A1D2C9F4B10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=jomwalimu
AppPublisherURL=https://github.com/jomwalimu/wi-pass
AppSupportURL=https://github.com/jomwalimu/wi-pass/issues
AppUpdatesURL=https://github.com/jomwalimu/wi-pass/releases
VersionInfoVersion={#MyAppVersionNumeric}
VersionInfoDescription=WiPass 설치
PrivilegesRequired=lowest
DefaultDirName={autopf}\WiPass
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
InfoBeforeFile=info-before.txt
OutputDir=..\dist
OutputBaseFilename=WiPass-Setup-{#MyAppVersion}
SetupIconFile=..\src\WiPass\wipass.ico
UninstallDisplayIcon={app}\{#MyAppExe}
UninstallDisplayName={#MyAppName}
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
MinVersion=10.0
CloseApplications=no

[Languages]
Name: "korean"; MessagesFile: "{#KoreanMessages}"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로가기 만들기"; Flags: unchecked

[Files]
Source: "{#BinDir}\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BinDir}\{#MyAppExe}.config"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "와이패스 실행하기"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';

{ Closes a running WiPass so its files can be replaced or removed. }
procedure CloseWiPass();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/f /im {#MyAppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseWiPass();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseWiPass();
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { WiPass registers itself to start with Windows; remove that entry. }
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'WiPass');

    DataDir := ExpandConstant('{userappdata}\WiPass');
    if DirExists(DataDir) and not UninstallSilent() then
    begin
      if MsgBox('저장된 장소·비밀번호와 기록도 함께 삭제할까요?' + #13#10 + #13#10 +
                '다시 설치해서 계속 쓰실 거라면 [아니요]를 누르세요.',
                mbConfirmation, MB_YESNO) = IDYES then
        DelTree(DataDir, True, True, True);
    end;
  end;
end;
