# Publish script for macOS (osx-arm64 and osx-x64) - self-contained single file
# Run from repository root in PowerShell

param(
    [string]$ProjectPath = "POTimeTracker.Avalonia/POTimeTracker.Avalonia.csproj",
    [string[]]$RIDs = @('osx-arm64','osx-x64'),
    [switch]$Trimmed
)

function Ensure-NuGetSource {
    $name = 'nuget.org'
    $url = 'https://api.nuget.org/v3/index.json'
    $sources = dotnet nuget list source --format short 2>$null | Select-String -Pattern "^\s*Name\s*:\s*(.+)$" -AllMatches
    $has = dotnet nuget list source 2>$null | Select-String -Pattern $url
    if (-not $has) {
        Write-Host "Adding nuget.org source..."
        dotnet nuget add source $url -n $name | Out-Null
    }
}

function Publish-RID {
    param($rid)
    $out = Join-Path -Path "publish" -ChildPath $rid
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Path $out | Out-Null

    $trimFlag = if ($Trimmed) { '-p:PublishTrimmed=true' } else { '-p:PublishTrimmed=false' }

    $args = @(
        'publish', $ProjectPath,
        '-c','Release',
        '-r',$rid,
        '--self-contained','true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        $trimFlag,
        '-o',$out
    )
    Write-Host "dotnet $($args -join ' ')"
    $p = Start-Process -FilePath dotnet -ArgumentList $args -NoNewWindow -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        throw "dotnet publish failed for $rid (exit $($p.ExitCode))"
    }

    # find the produced executable (largest file without .dll/.pdb/.json)
    $files = Get-ChildItem -Path $out | Where-Object { -not ($_.Name -like '*.dll' -or $_.Name -like '*.pdb' -or $_.Name -like '*.json' -or $_.Name -like '*.deps.json' -or $_.Name -like '*.runtimeconfig.json') }
    $exe = $files | Sort-Object Length -Descending | Select-Object -First 1
    if (-not $exe) { Write-Warning "No executable found in $out"; return }

    # create minimal .app bundle
    $appName = 'POTimeTracker'
    $appDir = Join-Path $out "$appName.app"
    $contents = Join-Path $appDir 'Contents'
    $macos = Join-Path $contents 'MacOS'
    $resources = Join-Path $contents 'Resources'
    New-Item -ItemType Directory -Path $macos -Force | Out-Null
    New-Item -ItemType Directory -Path $resources -Force | Out-Null

    $destExe = Join-Path $macos $exe.Name
    Copy-Item -Path $exe.FullName -Destination $destExe -Force
    # ensure executable bit
    & icacls $destExe 2>$null | Out-Null  # no-op on non-Windows
    try { & chmod +x $destExe } catch {}

    $plist = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>$appName</string>
  <key>CFBundleExecutable</key><string>$($exe.Name)</string>
  <key>CFBundleIdentifier</key><string>com.invenzis.potimetracker</string>
  <key>CFBundleVersion</key><string>1.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.productivity</string>
</dict>
</plist>
"@
    $plistPath = Join-Path $contents 'Info.plist'
    $plist | Out-File -FilePath $plistPath -Encoding utf8

    Write-Host "Created app bundle at: $appDir"
}

# Main
try {
    Write-Host "Ensuring nuget.org source is present..."
    Ensure-NuGetSource
    Write-Host "Restoring project..."
    dotnet restore $ProjectPath

    foreach ($rid in $RIDs) {
        Write-Host "Publishing for $rid..."
        Publish-RID -rid $rid
    }

    Write-Host "Publish completed. See ./publish folder."
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
