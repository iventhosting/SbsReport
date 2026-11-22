param(
    [string]$Version = "",
    [string]$ApiKey = $env:NUGET_API_KEY,
    [string]$Source = "https://api.nuget.org/v3/index.json"
)

$ErrorActionPreference = "Stop"
$csproj = Join-Path $PSScriptRoot "src\SbsReport\SbsReport.csproj"
$outDir = Join-Path $PSScriptRoot "nupkg"

# Opsiyonel: Versiyon verildiyse .csproj içindeki <Version> alanını güncelle
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $xml = Get-Content $csproj -Raw -Encoding UTF8
    $xml = $xml -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
    [System.IO.File]::WriteAllText($csproj, $xml, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "Versiyon güncellendi -> $Version" -ForegroundColor Cyan
}

# Güncel versiyonu oku
$curXml = Get-Content $csproj -Raw -Encoding UTF8
if ($curXml -match '<Version>([^<]+)</Version>') {
    $curVer = $Matches[1]
} else {
    throw "<Version> etiketi bulunamadı!"
}

Write-Host "Paket oluşturuluyor: SbsReport.$curVer.nupkg ..." -ForegroundColor Yellow
dotnet pack $csproj -c Release -o $outDir
$pkgPath = Join-Path $outDir "SbsReport.$curVer.nupkg"
Write-Host "Paket hazır: $pkgPath" -ForegroundColor Green

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host ""
    Write-Host "NuGet.org'a göndermek için API Key ile çalıştırın:" -ForegroundColor Yellow
    Write-Host "  .\publish-nuget.ps1 -Version $curVer -ApiKey YOUR_NUGET_API_KEY" -ForegroundColor White
    exit 0
}

Write-Host "NuGet'e yayınlanıyor ($Source)..." -ForegroundColor Cyan
dotnet nuget push $pkgPath --api-key $ApiKey --source $Source --skip-duplicate
Write-Host "Yayınlama tamamlandı: SbsReport $curVer" -ForegroundColor Green
