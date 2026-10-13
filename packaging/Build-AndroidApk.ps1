$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$AndroidDir = Join-Path $RepoRoot "android\app\src\main"
$DistDir = Join-Path $RepoRoot "dist"

$AndroidHome = "C:\Android"
$PlatformJar = "$AndroidHome\platforms\android-34\android.jar"
$BuildToolsDir = "$AndroidHome\build-tools\34.0.0"
$Aapt2 = "$BuildToolsDir\aapt2.exe"
$D8 = "$BuildToolsDir\d8.bat"
$ZipAlign = "$BuildToolsDir\zipalign.exe"
$ApkSigner = "$BuildToolsDir\apksigner.bat"

$BuildDir = Join-Path $env:TEMP "AetheriaApkBuild"
if (Test-Path $BuildDir) { Remove-Item $BuildDir -Recurse -Force }
New-Item -ItemType Directory -Path $BuildDir -Force | Out-Null

$resCompiled = Join-Path $BuildDir "res.zip"
$genDir = Join-Path $BuildDir "gen"
$classesDir = Join-Path $BuildDir "classes"
$dexDir = Join-Path $BuildDir "dex"
New-Item -ItemType Directory -Path $genDir, $classesDir, $dexDir -Force | Out-Null

# 1. Compile resources with aapt2
Write-Host "=== [1/7] Compiling Android Resources ===" -ForegroundColor Cyan
& $Aapt2 compile --dir (Join-Path $AndroidDir "res") -o $resCompiled

# 2. Link resources and generate R.java
Write-Host "=== [2/7] Linking Resources & Manifest ===" -ForegroundColor Cyan
$protoApk = Join-Path $BuildDir "proto.apk"
& $Aapt2 link -I $PlatformJar --manifest (Join-Path $AndroidDir "AndroidManifest.xml") $resCompiled -o $protoApk --java $genDir

# 3. Compile Java source files
Write-Host "=== [3/7] Compiling Java Sources with javac ===" -ForegroundColor Cyan
$javaFiles = Get-ChildItem -Path (Join-Path $AndroidDir "java"), $genDir -Recurse -Filter *.java | Select-Object -ExpandProperty FullName
& javac --release 17 -cp $PlatformJar -d $classesDir $javaFiles

# 4. Convert classes to DEX with D8
Write-Host "=== [4/7] Converting Bytecode to DEX with D8 ===" -ForegroundColor Cyan
$classFiles = Get-ChildItem -Path $classesDir -Recurse -Filter *.class | Select-Object -ExpandProperty FullName
& $D8 --lib $PlatformJar --output $dexDir $classFiles

# 5. Add classes.dex into APK archive
Write-Host "=== [5/7] Adding classes.dex to APK ===" -ForegroundColor Cyan
& jar uf $protoApk -C $dexDir classes.dex

# 6. Align APK with zipalign
Write-Host "=== [6/7] Running zipalign 4-byte boundary ===" -ForegroundColor Cyan
$alignedApk = Join-Path $BuildDir "aligned.apk"
& $ZipAlign -f -p 4 $protoApk $alignedApk

# 7. Sign APK with apksigner
Write-Host "=== [7/7] Signing APK with apksigner ===" -ForegroundColor Cyan
$keystoreDir = Join-Path $env:USERPROFILE ".android"
$keystore = Join-Path $keystoreDir "debug.keystore"
if (-not (Test-Path $keystore)) {
    New-Item -ItemType Directory -Path $keystoreDir -Force -ErrorAction SilentlyContinue | Out-Null
    & keytool -genkeypair -v -keystore $keystore -storepass android -alias androiddebugkey -keypass android -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=Android Debug,O=Android,C=US" | Out-Null
}

$finalApk = Join-Path $DistDir "Aetheria-app.apk"
if (Test-Path $finalApk) { Remove-Item $finalApk -Force }

& $ApkSigner sign --ks $keystore --ks-pass pass:android --ks-key-alias androiddebugkey --v1-signing-enabled true --v2-signing-enabled true --v3-signing-enabled true --out $finalApk $alignedApk

# Also copy a friendly versioned name
$versionedApk = Join-Path $DistDir "Aetheria-Roleplay-v1.0.1.apk"
Copy-Item $finalApk $versionedApk -Force

Write-Host "=== Android APK Successfully Created! ===" -ForegroundColor Green
Get-Item $finalApk, $versionedApk | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
