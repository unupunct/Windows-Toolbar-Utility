# Runs the tests, publishes a self-contained single-file UnupunctWinTab.exe and builds the Inno Setup installer into .\release
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
dotnet test tests/WinTab.Tests -c Release --nologo
if ($LASTEXITCODE) { throw 'tests failed' }
Remove-Item publish -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src/WinTab.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish --nologo
if ($LASTEXITCODE) { throw 'publish failed' }
New-Item -ItemType Directory -Force release | Out-Null
Copy-Item publish\UnupunctWinTab.exe release\UnupunctWinTab.exe -Force
$extra = Get-ChildItem publish -File | Where-Object { $_.Name -ne 'UnupunctWinTab.exe' -and $_.Extension -ne '.pdb' }
if ($extra) { throw "Publish output contains extra files: $($extra.Name -join ', ')" }
$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found' }
& $iscc installer\UnupunctWinTab.iss
Get-FileHash release\*.exe -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLower())  $(Split-Path $_.Path -Leaf)" } | Set-Content release\SHA256SUMS.txt
Get-ChildItem release | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
