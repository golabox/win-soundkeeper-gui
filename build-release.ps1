<#
    Build Release officiel de Sound Keeper GUI (x64), à lancer depuis la racine du dépôt :

        .\build-release.ps1

    Moteur C++ (MSBuild) -> GUI -> tests -> publication win-x64 -> vérifications -> raccourci local.
    S'arrête à la première erreur. Quitter d'abord Sound Keeper GUI s'il tourne depuis publish\win-x64.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$guiDirectory = Join-Path $root 'SoundKeeper.GUI'
$guiProject = Join-Path $guiDirectory 'SoundKeeper.GUI.csproj'
$testsProject = Join-Path $root 'SoundKeeper.GUI.Tests\SoundKeeper.GUI.Tests.csproj'
$publishDirectory = Join-Path $guiDirectory 'publish\win-x64'
$engine = Join-Path $root 'Bin\SoundKeeper64.exe'
$releaseX64 = @('-c', 'Release', '-r', 'win-x64', '-p:Platform=x64')

function Write-Step([string]$Text) {
    Write-Host ''
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Stop-Build([string]$Message) {
    Write-Host ''
    Write-Host "ÉCHEC : $Message" -ForegroundColor Red
    exit 1
}

function Invoke-Tool([string]$Description, [string]$FilePath, [string[]]$Arguments) {
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { Stop-Build "$Description (code $LASTEXITCODE)." }
}

function Assert-Published([string]$Expected, [string]$Published) {
    if (-not (Test-Path -LiteralPath $Expected)) { Stop-Build "fichier attendu introuvable : $Expected" }
    if (-not (Test-Path -LiteralPath $Published)) { Stop-Build "absent de la publication : $Published" }
    if ((Get-FileHash -LiteralPath $Expected).Hash -ne (Get-FileHash -LiteralPath $Published).Hash) {
        Stop-Build "publication différente de la compilation : $Published"
    }
}

Write-Step '0/9 Prérequis'
$running = @(Get-Process -Name 'SoundKeeper.GUI' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($publishDirectory, [StringComparison]::OrdinalIgnoreCase) })
if ($running.Count -gt 0) {
    Stop-Build 'Sound Keeper GUI tourne depuis publish\win-x64 : le quitter (zone de notification > Quitter), puis relancer ce script.'
}
if (-not (Get-Command 'dotnet' -ErrorAction SilentlyContinue)) { Stop-Build 'SDK .NET introuvable (commande dotnet).' }
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) {
    Stop-Build 'vswhere introuvable : installer Visual Studio ou Build Tools 2026 avec la charge C++ Desktop.'
}
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild) { Stop-Build 'MSBuild avec les outils C++ x64 introuvable (Visual Studio ou Build Tools 2026, charge C++ Desktop).' }

Write-Step '1/9 Moteur C++ (MSBuild, Release|x64)'
# Pré-build upstream désactivé : BuildInfo.cmd réécrirait BuildInfo.hpp, donc la version du moteur.
Invoke-Tool 'Compilation du moteur' $msbuild @(
    (Join-Path $root 'SoundKeeper.vcxproj'), '/nologo', '/v:minimal',
    '/p:Configuration=Release', '/p:Platform=x64', '/p:PreBuildEventUseInBuild=false')
if (-not (Test-Path -LiteralPath $engine)) { Stop-Build "moteur absent après compilation : $engine" }

Write-Step '2/9 GUI (dotnet build, Release|x64)'
Invoke-Tool 'Compilation du GUI' 'dotnet' (@('build', $guiProject, '--nologo') + $releaseX64)

Write-Step '3/9 Tests'
Invoke-Tool 'Tests' 'dotnet' @('run', '--project', $testsProject, '-c', 'Release', '-p:Platform=x64')

Write-Step '4/9 Publication win-x64'
Invoke-Tool 'Publication' 'dotnet' (@('publish', $guiProject, '--nologo', '--self-contained', 'true') + $releaseX64)
$targetDirectory = & dotnet msbuild $guiProject -nologo -getProperty:TargetDir -p:Configuration=Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 |
    Select-Object -Last 1
if ($LASTEXITCODE -ne 0 -or -not $targetDirectory -or -not (Test-Path -LiteralPath $targetDirectory.Trim())) {
    Stop-Build "dossier de compilation du GUI introuvable ($targetDirectory)."
}
$targetDirectory = $targetDirectory.Trim()
foreach ($file in 'SoundKeeper.GUI.exe', 'SoundKeeper.GUI.dll', 'Assets\soundkeeper.ico', 'Assets\soundkeeper-tray.ico', 'Assets\soundkeeper-app.svg') {
    Assert-Published (Join-Path $targetDirectory $file) (Join-Path $publishDirectory $file)
}
if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory 'SoundKeeper.GUI.published'))) {
    Stop-Build 'marqueur SoundKeeper.GUI.published absent : le démarrage Windows ne reconnaîtrait pas cette publication.'
}

Write-Step '5/9 Vérification Engine'
Assert-Published $engine (Join-Path $publishDirectory 'Engine\SoundKeeper64.exe')

Write-Step '6/9 Vérification Strings'
$strings = @(Get-ChildItem -LiteralPath (Join-Path $guiDirectory 'Strings') -Recurse -Filter 'Resources.resw')
if ($strings.Count -eq 0) { Stop-Build 'aucune ressource Strings\<langue>\Resources.resw.' }
foreach ($resw in $strings) {
    Assert-Published $resw.FullName (Join-Path $publishDirectory "Strings\$($resw.Directory.Name)\Resources.resw")
}

Write-Step '7/9 Vérification PRI'
Assert-Published (Join-Path $targetDirectory 'SoundKeeper.GUI.pri') (Join-Path $publishDirectory 'SoundKeeper.GUI.pri')

Write-Step '8/9 Vérification XBF'
$xamlFiles = @(Get-ChildItem -LiteralPath $guiDirectory -Recurse -Filter '*.xaml' |
    Where-Object { $_.FullName.Substring($guiDirectory.Length + 1) -notmatch '^(bin|obj|publish)\\' })
if ($xamlFiles.Count -eq 0) { Stop-Build 'aucun fichier XAML trouvé.' }
foreach ($xaml in $xamlFiles) {
    $xbf = [IO.Path]::ChangeExtension($xaml.FullName.Substring($guiDirectory.Length + 1), '.xbf')
    Assert-Published (Join-Path $targetDirectory $xbf) (Join-Path $publishDirectory $xbf)
}

Write-Step '9/9 Raccourci local'
$executable = Join-Path $publishDirectory 'SoundKeeper.GUI.exe'
$shortcutPath = Join-Path $root 'Lancer Sound Keeper GUI.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executable
$shortcut.Arguments = ''
$shortcut.WorkingDirectory = $publishDirectory
$shortcut.IconLocation = "$executable,0"
$shortcut.Save()

$version = (Get-Item -LiteralPath $executable).VersionInfo.ProductVersion
Write-Host ''
Write-Host "Candidate Sound Keeper GUI $version prête : $publishDirectory" -ForegroundColor Green
Write-Host "Raccourci : $shortcutPath"
