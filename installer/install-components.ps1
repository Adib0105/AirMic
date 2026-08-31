param(
    [Parameter(Mandatory = $true)][ValidateSet('Install', 'Uninstall')][string]$Action,
    [Parameter(Mandatory = $true)][string]$InstallDirectory,
    [string]$DriverInf
)

$ErrorActionPreference = 'Stop'
$stateDirectory = Join-Path $env:ProgramData 'AirMic'
$driverState = Join-Path $stateDirectory 'driver-package.txt'
$controlRule = 'AirMic Private LAN Control'
$audioRule = 'AirMic Private LAN Audio'

function Invoke-PnpUtil([string[]]$Arguments) {
    $process = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\pnputil.exe') -ArgumentList $Arguments -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) { throw "Windows rejected the AirMic audio driver operation (exit $($process.ExitCode))." }
}

if ($Action -eq 'Install') {
    if (-not $DriverInf -or -not (Test-Path -LiteralPath $DriverInf)) { throw 'The signed AirMic driver package is missing.' }
    $catalog = Get-ChildItem -LiteralPath (Split-Path $DriverInf -Parent) -Filter '*.cat' |
        Where-Object { (Get-AuthenticodeSignature -LiteralPath $_.FullName).Status -eq 'Valid' } | Select-Object -First 1
    if (-not $catalog) { throw 'The AirMic driver package has no catalog signed by a trusted publisher.' }
    New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
    Invoke-PnpUtil @('/add-driver', $DriverInf, '/install')
    $originalName = Split-Path $DriverInf -Leaf
    $driver = Get-WindowsDriver -Online -All | Where-Object { (Split-Path $_.OriginalFileName -Leaf) -eq $originalName } | Select-Object -First 1
    if (-not $driver) { throw 'Windows installed the driver but its published package name could not be verified.' }
    Set-Content -LiteralPath $driverState -Value $driver.Driver -Encoding ASCII

    $program = Join-Path $InstallDirectory 'AirMic.exe'
    Remove-NetFirewallRule -DisplayName $controlRule -ErrorAction SilentlyContinue
    Remove-NetFirewallRule -DisplayName $audioRule -ErrorAction SilentlyContinue
    New-NetFirewallRule -DisplayName $controlRule -Direction Inbound -Action Allow -Profile Private -Program $program -Protocol TCP -LocalPort 51243 | Out-Null
    New-NetFirewallRule -DisplayName $audioRule -Direction Inbound -Action Allow -Profile Private -Program $program -Protocol UDP -LocalPort 51244 | Out-Null
    exit 0
}

Remove-NetFirewallRule -DisplayName $controlRule -ErrorAction SilentlyContinue
Remove-NetFirewallRule -DisplayName $audioRule -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $driverState) {
    $publishedName = (Get-Content -LiteralPath $driverState -Raw).Trim()
    if ($publishedName -match '^oem\d+\.inf$') { Invoke-PnpUtil @('/delete-driver', $publishedName, '/uninstall') }
}
Remove-Item -LiteralPath $stateDirectory -Recurse -Force -ErrorAction SilentlyContinue
