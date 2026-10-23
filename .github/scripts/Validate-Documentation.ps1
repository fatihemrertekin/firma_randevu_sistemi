param([string]$RepositoryRoot = (Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath($RepositoryRoot)
Set-Location -LiteralPath $repoRoot
$paths = @(git -c core.quotePath=false ls-files -- '*.md')
if ($LASTEXITCODE -ne 0) { throw 'Belge listesi alınamadı.' }
$contentCache = @{}
$anchorCache = @{}
function ReadDocument([string]$path) {
    if (!$contentCache.ContainsKey($path)) {
        $absolutePath = Join-Path $repoRoot $path
        $contentCache[$path] = [IO.File]::ReadAllText($absolutePath)
    }
    return $contentCache[$path]
}
function GetAnchors([string]$path) {
    if (!$anchorCache.ContainsKey($path)) {
        $anchors = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $counts = @{}
        foreach ($heading in [regex]::Matches((ReadDocument $path), '(?m)^#{1,6}\s+(.+?)\s*\r?$')) {
            $slug = $heading.Groups[1].Value.Replace('İ', 'i').ToLowerInvariant()
            $slug = [regex]::Replace($slug, '[^\p{L}\p{M}\p{N}\p{Z}\s_-]', '')
            $slug = [regex]::Replace($slug.Trim(), '\s', '-')
            if ($counts.ContainsKey($slug)) { $counts[$slug]++; $slug += '-' + $counts[$slug] }
            else { $counts[$slug] = 0 }
            [void]$anchors.Add($slug)
        }
        foreach ($anchor in [regex]::Matches((ReadDocument $path), '(?:id|name)="([^"]+)"')) { [void]$anchors.Add($anchor.Groups[1].Value) }
        $anchorCache[$path] = $anchors
    }
    return ,$anchorCache[$path]
}
$failures = [Collections.Generic.List[string]]::new()
$links = 0
foreach ($path in $paths) {
    $sourceDirectory = Split-Path (Join-Path $repoRoot $path) -Parent
    foreach ($match in [regex]::Matches((ReadDocument $path), '\]\(([^\s)]+)\)')) {
        $target = $match.Groups[1].Value
        if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') { continue }
        $links++
        $parts = $target.Split('#', 2)
        $absolutePath = if ($parts[0] -eq '') { Join-Path $repoRoot $path } else { [IO.Path]::GetFullPath((Join-Path $sourceDirectory $parts[0])) }
        if (!(Test-Path -LiteralPath $absolutePath)) { $failures.Add("$path -> $target (dosya yok)"); continue }
        if ($parts.Count -eq 2 -and $parts[1] -ne '' -and $absolutePath.EndsWith('.md')) {
            $targetPath = [IO.Path]::GetRelativePath($repoRoot, $absolutePath).Replace('\', '/')
            $anchor = [Uri]::UnescapeDataString($parts[1])
            if (!(GetAnchors $targetPath).Contains($anchor)) { $failures.Add("$path -> $target (başlık yok)") }
        }
    }
}
if ($failures.Count -gt 0) { $failures | Write-Output; throw "$($failures.Count) geçersiz bağlantı." }
Write-Output "$($paths.Count) Markdown belgesinde $links yerel dosya/başlık bağlantısı doğrulandı."
