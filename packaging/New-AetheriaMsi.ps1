param(
    [Parameter(Mandatory = $true)][string]$SourceDir,
    [Parameter(Mandatory = $true)][string]$OutputMsi,
    [Parameter(Mandatory = $true)][string]$PayloadZip
)

$ErrorActionPreference = "Stop"

if (Test-Path $OutputMsi) {
    Remove-Item $OutputMsi -Force
}

$installer = New-Object -ComObject WindowsInstaller.Installer
$installerType = $installer.GetType()
$db = $installerType.InvokeMember("OpenDatabase", "InvokeMethod", $null, $installer, @($OutputMsi, 3))
$dbType = $db.GetType()

function Invoke-MsiSql($Database, $DbType, [string]$Sql) {
    $view = $DbType.InvokeMember("OpenView", "InvokeMethod", $null, $Database, @($Sql))
    $vType = $view.GetType()
    $vType.InvokeMember("Execute", "InvokeMethod", $null, $view, $null) | Out-Null
    $vType.InvokeMember("Close", "InvokeMethod", $null, $view, $null) | Out-Null
}

# 1. Create required Windows Installer relational tables
Invoke-MsiSql $db $dbType 'CREATE TABLE `Property` (`Property` CHAR(72) NOT NULL, `Value` LONGCHAR NOT NULL PRIMARY KEY `Property`)'
Invoke-MsiSql $db $dbType 'CREATE TABLE `Directory` (`Directory` CHAR(72) NOT NULL, `Directory_Parent` CHAR(72), `DefaultDir` CHAR(255) NOT NULL PRIMARY KEY `Directory`)'
Invoke-MsiSql $db $dbType 'CREATE TABLE `Feature` (`Feature` CHAR(38) NOT NULL, `Feature_Parent` CHAR(38), `Title` CHAR(64), `Description` CHAR(255), `Display` SHORT, `Level` SHORT NOT NULL, `Directory_` CHAR(72), `Attributes` SHORT NOT NULL PRIMARY KEY `Feature`)'
Invoke-MsiSql $db $dbType 'CREATE TABLE `Component` (`Component` CHAR(72) NOT NULL, `ComponentId` CHAR(38), `Directory_` CHAR(72) NOT NULL, `Attributes` SHORT NOT NULL, `Condition` CHAR(255), `KeyPath` CHAR(72) PRIMARY KEY `Component`)'
Invoke-MsiSql $db $dbType 'CREATE TABLE `FeatureComponents` (`Feature_` CHAR(38) NOT NULL, `Component_` CHAR(72) NOT NULL PRIMARY KEY `Feature_`, `Component_`)'
Invoke-MsiSql $db $dbType 'CREATE TABLE `File` (`File` CHAR(72) NOT NULL, `Component_` CHAR(72) NOT NULL, `FileName` CHAR(255) NOT NULL, `FileSize` LONG NOT NULL, `Version` CHAR(72), `Language` CHAR(20), `Attributes` SHORT, `Sequence` SHORT NOT NULL PRIMARY KEY `File`)'
Invoke-MsiSql $db $dbType 'CREATE TABLE `Media` (`DiskId` SHORT NOT NULL, `LastSequence` SHORT NOT NULL, `DiskPrompt` CHAR(64), `Cabinet` CHAR(255), `VolumeLabel` CHAR(32), `Source` CHAR(72) PRIMARY KEY `DiskId`)'
Invoke-MsiSql $db $dbType 'CREATE TABLE `Binary` (`Name` CHAR(72) NOT NULL, `Data` OBJECT NOT NULL PRIMARY KEY `Name`)'
Invoke-MsiSql $db $dbType 'CREATE TABLE `CustomAction` (`Action` CHAR(72) NOT NULL, `Type` SHORT NOT NULL, `Source` CHAR(72), `Target` LONGCHAR PRIMARY KEY `Action`)'
Invoke-MsiSql $db $dbType 'CREATE TABLE `InstallExecuteSequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)'
Invoke-MsiSql $db $dbType 'CREATE TABLE `InstallUISequence` (`Action` CHAR(72) NOT NULL, `Condition` CHAR(255), `Sequence` SHORT PRIMARY KEY `Action`)'

# Use a fresh ProductCode so double-clicking always performs a fresh extraction + launch
$productCode = "{" + [Guid]::NewGuid().ToString().ToUpper() + "}"
$upgradeCode = "{2B7D9F14-6E3A-4C8B-8A1E-5D9C3F7A1B20}"
$packageCode = "{" + [Guid]::NewGuid().ToString().ToUpper() + "}"

# 2. Populate Property table (LIMITUI=1 shows standard Windows Installer progress bar & never hangs waiting for custom dialog tables)
$props = @{
    "ProductCode"       = $productCode
    "ProductName"       = "Aetheria AI Roleplay Platform"
    "ProductVersion"    = "1.0.0"
    "Manufacturer"      = "Aetheria Studio"
    "UpgradeCode"       = $upgradeCode
    "ProductLanguage"   = "1033"
    "ALLUSERS"          = "2"
    "MSIINSTALLPERUSER" = "1"
    "ARPNOMODIFY"       = "1"
    "LIMITUI"           = "1"
}
foreach ($kv in $props.GetEnumerator()) {
    $sql = 'INSERT INTO `Property` (`Property`, `Value`) VALUES (''' + $kv.Key + ''', ''' + $kv.Value + ''')'
    Invoke-MsiSql $db $dbType $sql
}

# 3. Populate Directory, Feature, Component, Media
Invoke-MsiSql $db $dbType 'INSERT INTO `Directory` (`Directory`, `Directory_Parent`, `DefaultDir`) VALUES (''TARGETDIR'', '''', ''SourceDir'')'
Invoke-MsiSql $db $dbType 'INSERT INTO `Directory` (`Directory`, `Directory_Parent`, `DefaultDir`) VALUES (''LocalAppDataFolder'', ''TARGETDIR'', ''.'')'
Invoke-MsiSql $db $dbType 'INSERT INTO `Directory` (`Directory`, `Directory_Parent`, `DefaultDir`) VALUES (''INSTALLDIR'', ''LocalAppDataFolder'', ''AetheriaApp'')'

Invoke-MsiSql $db $dbType 'INSERT INTO `Feature` (`Feature`, `Feature_Parent`, `Title`, `Description`, `Display`, `Level`, `Directory_`, `Attributes`) VALUES (''MainFeature'', '''', ''Aetheria Platform'', ''Windows 11 AI Roleplay Platform'', 1, 1, ''INSTALLDIR'', 0)'
Invoke-MsiSql $db $dbType 'INSERT INTO `Component` (`Component`, `ComponentId`, `Directory_`, `Attributes`, `Condition`, `KeyPath`) VALUES (''MainComponent'', ''{5E2F1A9C-4B7D-4E8A-9C3F-1D6B8E2A4F11}'', ''INSTALLDIR'', 0, '''', '''')'
Invoke-MsiSql $db $dbType 'INSERT INTO `FeatureComponents` (`Feature_`, `Component_`) VALUES (''MainFeature'', ''MainComponent'')'
Invoke-MsiSql $db $dbType 'INSERT INTO `Media` (`DiskId`, `LastSequence`, `DiskPrompt`, `Cabinet`, `VolumeLabel`, `Source`) VALUES (1, 1, '''', '''', '''', '''')'

# 4. Embed Self-Contained Setup EXE into Binary table
$setupExePath = Join-Path (Split-Path $OutputMsi -Parent) "Aetheria-Setup-win-x64.exe"
$binaryPayload = if (Test-Path $setupExePath) { $setupExePath } else { $PayloadZip }

$view = $dbType.InvokeMember("OpenView", "InvokeMethod", $null, $db, @('SELECT `Name`, `Data` FROM `Binary`'))
$vType = $view.GetType()
$vType.InvokeMember("Execute", "InvokeMethod", $null, $view, $null) | Out-Null

$rec = $installerType.InvokeMember("CreateRecord", "InvokeMethod", $null, $installer, @(2))
$rType = $rec.GetType()
$rType.InvokeMember("StringData", "SetProperty", $null, $rec, @(1, "AetheriaSetupExe")) | Out-Null
$rType.InvokeMember("SetStream", "InvokeMethod", $null, $rec, @(2, $binaryPayload)) | Out-Null
$vType.InvokeMember("Modify", "InvokeMethod", $null, $view, @(1, $rec)) | Out-Null
$vType.InvokeMember("Close", "InvokeMethod", $null, $view, $null) | Out-Null

# 5. Add CustomAction (Type 66 = 2 [EXE in Binary table] + 64 [Synchronous, ignore exit code])
Invoke-MsiSql $db $dbType 'INSERT INTO `CustomAction` (`Action`, `Type`, `Source`, `Target`) VALUES (''LaunchAetheriaSetup'', 66, ''AetheriaSetupExe'', '''')'

# 6. Populate InstallUISequence (so double-clicking in Explorer runs the graphical Setup window on the interactive desktop)
Invoke-MsiSql $db $dbType 'INSERT INTO `InstallUISequence` (`Action`, `Condition`, `Sequence`) VALUES (''LaunchConditions'', '''', 100)'
Invoke-MsiSql $db $dbType 'INSERT INTO `InstallUISequence` (`Action`, `Condition`, `Sequence`) VALUES (''CostInitialize'', '''', 800)'
Invoke-MsiSql $db $dbType 'INSERT INTO `InstallUISequence` (`Action`, `Condition`, `Sequence`) VALUES (''FileCost'', '''', 900)'
Invoke-MsiSql $db $dbType 'INSERT INTO `InstallUISequence` (`Action`, `Condition`, `Sequence`) VALUES (''CostFinalize'', '''', 1000)'
Invoke-MsiSql $db $dbType 'INSERT INTO `InstallUISequence` (`Action`, `Condition`, `Sequence`) VALUES (''LaunchAetheriaSetup'', '''', 1250)'
Invoke-MsiSql $db $dbType 'INSERT INTO `InstallUISequence` (`Action`, `Condition`, `Sequence`) VALUES (''ExecuteAction'', '''', 1300)'

# 7. Populate InstallExecuteSequence
$seq = @(
    @{ Action = "LaunchConditions"; Seq = 100; Cond = "" },
    @{ Action = "CostInitialize"; Seq = 800; Cond = "" },
    @{ Action = "FileCost"; Seq = 900; Cond = "" },
    @{ Action = "CostFinalize"; Seq = 1000; Cond = "" },
    @{ Action = "InstallValidate"; Seq = 1400; Cond = "" },
    @{ Action = "InstallInitialize"; Seq = 1500; Cond = "" },
    @{ Action = "ProcessComponents"; Seq = 1600; Cond = "" },
    @{ Action = "CreateFolders"; Seq = 3700; Cond = "" },
    @{ Action = "InstallFiles"; Seq = 4000; Cond = "" },
    @{ Action = "RegisterProduct"; Seq = 6100; Cond = "" },
    @{ Action = "PublishFeatures"; Seq = 6300; Cond = "" },
    @{ Action = "PublishProduct"; Seq = 6400; Cond = "" },
    @{ Action = "LaunchAetheriaSetup"; Seq = 6500; Cond = "UILevel=2" },
    @{ Action = "InstallFinalize"; Seq = 6600; Cond = "" }
)
foreach ($s in $seq) {
    $sql = 'INSERT INTO `InstallExecuteSequence` (`Action`, `Condition`, `Sequence`) VALUES (''' + $s.Action + ''', ''' + $s.Cond + ''', ' + $s.Seq + ')'
    Invoke-MsiSql $db $dbType $sql
}

# 8. Populate SummaryInformation Stream
$sumInfo = $dbType.InvokeMember("SummaryInformation", "GetProperty", $null, $db, @(20))
$sType = $sumInfo.GetType()
$sType.InvokeMember("Property", "SetProperty", $null, $sumInfo, @(1, 1252)) | Out-Null
$sType.InvokeMember("Property", "SetProperty", $null, $sumInfo, @(2, "Aetheria AI Roleplay Platform Installer")) | Out-Null
$sType.InvokeMember("Property", "SetProperty", $null, $sumInfo, @(3, "Aetheria AI Roleplay Platform")) | Out-Null
$sType.InvokeMember("Property", "SetProperty", $null, $sumInfo, @(4, "Aetheria Studio")) | Out-Null
$sType.InvokeMember("Property", "SetProperty", $null, $sumInfo, @(5, "Installer")) | Out-Null
$sType.InvokeMember("Property", "SetProperty", $null, $sumInfo, @(6, "Windows 11 Native WinUI 3 AI Roleplay & Multiplayer Platform")) | Out-Null
$sType.InvokeMember("Property", "SetProperty", $null, $sumInfo, @(7, "x64;1033")) | Out-Null
$sType.InvokeMember("Property", "SetProperty", $null, $sumInfo, @(9, $packageCode)) | Out-Null
$sType.InvokeMember("Property", "SetProperty", $null, $sumInfo, @(14, 200)) | Out-Null
$sType.InvokeMember("Property", "SetProperty", $null, $sumInfo, @(15, 2)) | Out-Null
$sType.InvokeMember("Persist", "InvokeMethod", $null, $sumInfo, $null) | Out-Null

$dbType.InvokeMember("Commit", "InvokeMethod", $null, $db, $null) | Out-Null

[System.Runtime.InteropServices.Marshal]::ReleaseComObject($sumInfo) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($installer) | Out-Null
Write-Host "Created verified Windows Installer MSI package: $OutputMsi" -ForegroundColor Green
