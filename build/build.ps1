﻿param (
	[Parameter(Mandatory=$true)]
	[ValidatePattern("^\d+\.\d+.\d+.\d+")]
	[string]
	$ReleaseVersionNumber
)

$PSScriptFilePath = Get-Item $MyInvocation.MyCommand.Path
$RepoRoot = $PSScriptFilePath.Directory.Parent.FullName
$NuGetFolder = Join-Path -Path $RepoRoot "packages"
$SolutionPath = Join-Path -Path $RepoRoot -ChildPath "letsencrypt-win-simple.sln"
$BuildFolder = Join-Path -Path $RepoRoot -ChildPath "build"
$ProjectRoot = Join-Path -Path $RepoRoot "letsencrypt-win-simple"
$TempFolder = Join-Path -Path $BuildFolder -ChildPath "temp"
$Configuration = "Release"
$ReleaseOutputFolder = Join-Path -Path $ProjectRoot -ChildPath "bin/$Configuration"
$MSBuild = "${Env:ProgramFiles(x86)}\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MsBuild.exe"

# Go get nuget.exe if we don't have it
$NuGet = "$BuildFolder\nuget.exe"
$FileExists = Test-Path $NuGet 
If ($FileExists -eq $False) {
	$SourceNugetExe = "https://dist.nuget.org/win-x86-commandline/latest/nuget.exe"
	Invoke-WebRequest $SourceNugetExe -OutFile $NuGet
}

# Restore NuGet packages
cmd.exe /c "$NuGet restore $SolutionPath -NonInteractive -PackagesDirectory $NuGetFolder"

# Set the version number in SolutionInfo.cs
$versionParts = $ReleaseVersionNumber.Split(".")
$NewVersion = 'AssemblyVersion("' + $versionParts[0] + $versionParts[1] + $versionParts[2] + '.' + $versionParts[3] + '.*")'
$NewFileVersion = 'AssemblyFileVersion("' + $ReleaseVersionNumber + '")'

$SolutionInfoPath = Join-Path -Path $ProjectRoot -ChildPath "Properties/AssemblyInfo.cs"
(gc -Path $SolutionInfoPath) `
	-replace 'AssemblyVersion\("[0-9\.*]+"\)', $NewVersion |
	sc -Path $SolutionInfoPath -Encoding UTF8
(gc -Path $SolutionInfoPath) `
	-replace 'AssemblyFileVersion\("[0-9\.]+"\)', "$NewFileVersion" |
	sc -Path $SolutionInfoPath -Encoding UTF8

# Build the solution in release mode

# Clean solution
& $MSBuild "$SolutionPath" /p:Configuration=$Configuration /maxcpucount /t:Clean
if (-not $?)
{
	throw "The MSBuild process returned an error code."
}

# Build
& $MSBuild "$SolutionPath" /p:Configuration=$Configuration /maxcpucount
if (-not $?)
{
	throw "The MSBuild process returned an error code."
}

# Copy release files
if (Test-Path $TempFolder) 
{
    Remove-Item $TempFolder -Recurse
}
New-Item $TempFolder -Type Directory

$DestinationZipFile = "$BuildFolder\letsencrypt-win-simple.v$ReleaseVersionNumber.zip" 
if (Test-Path $DestinationZipFile) 
{
    Remove-Item $DestinationZipFile
}

Copy-Item (Join-Path -Path $ReleaseOutputFolder -ChildPath "scripts") (Join-Path -Path $TempFolder -ChildPath "scripts") -Recurse
Copy-Item (Join-Path -Path $ReleaseOutputFolder "letsencrypt.exe") $TempFolder
Copy-Item (Join-Path -Path $ReleaseOutputFolder "version.txt") $TempFolder
Copy-Item (Join-Path -Path $ReleaseOutputFolder "letsencrypt.exe.config") $TempFolder
Copy-Item (Join-Path -Path $ReleaseOutputFolder "Web_Config.xml") $TempFolder

# Zip the package
Add-Type -assembly "system.io.compression.filesystem"
[io.compression.zipfile]::CreateFromDirectory($TempFolder, $DestinationZipFile) 