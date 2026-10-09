<#
.SYNOPSIS
    Follow up pass to Find-SharedParameterUsage.ps1. Finds other name to GUID parameter
    catalog classes beyond RoomClassificationParameterCatalog, finds code that touches the
    shared parameter file or BindingMap directly, then dumps the full source of every file
    it flags into one text file for review.

.DESCRIPTION
    Regex based, same limitations as the first script apply: dynamic names are invisible,
    and a multi line "new SomeParameterDefinition(" call may only be partially captured by
    the single line ConstructorCall pattern. That is fine here, because any file with a hit
    gets its full source dumped rather than just the matching line, so the actual constructor
    body is still there to read.

.PARAMETER SolutionRoot
    Root folder to scan recursively for .cs files.

.PARAMETER OutputCsv
    Where the match list (class declarations, constructor calls, shared parameter file API
    touches) gets written.

.PARAMETER OutputDump
    Where the full source dump of every flagged file gets written.

.EXAMPLE
    .\Find-SharedParameterCatalogs.ps1
    Uses the default BA Tools solution path and writes both output files next to the script.
#>

param(
    [string]$SolutionRoot = "C:\Users\mpocinek\Desktop\Add\BA\BA",
    [string]$OutputCsv = "$PSScriptRoot\ParameterCatalogsAndApiUsage.csv",
    [string]$OutputDump = "$PSScriptRoot\ParameterCatalogFilesDump.txt"
)

if (-not (Test-Path -LiteralPath $SolutionRoot)) {
    Write-Error "SolutionRoot not found: $SolutionRoot"
    exit 1
}

$excludeDirPatterns = @('\\bin\\', '\\obj\\', '\\packages\\', '\\\.git\\')

$files = Get-ChildItem -Path $SolutionRoot -Filter *.cs -Recurse -File |
    Where-Object {
        $full = $_.FullName
        -not ($excludeDirPatterns | Where-Object { $full -match $_ })
    }

Write-Host "Scanning $($files.Count) .cs files under $SolutionRoot ..."

$patterns = @(
    @{ Name = 'ClassDeclaration';   Regex = '\bclass\s+(\w*Parameter(?:Definition|Catalog)\w*)\b' }
    @{ Name = 'ConstructorCall';    Regex = '\bnew\s+(\w*Parameter(?:Definition|Catalog)\w*)\s*\(' }
    @{ Name = 'OpenSharedParamFile';Regex = '(OpenSharedParameterFile)\s*\(' }
    @{ Name = 'SharedParamFilename';Regex = '(SharedParametersFilename)\b' }
    @{ Name = 'DefinitionGroups';   Regex = '(DefinitionGroups)\b' }
    @{ Name = 'BindingMap';         Regex = '(BindingMap|ParameterBindings)\b' }
    @{ Name = 'ExternalDefCreate';  Regex = '(ExternalDefinitionCreationOptions)\b' }
    @{ Name = 'ExternalDefinition'; Regex = '\b(ExternalDefinition)\b' }
)

$results = New-Object System.Collections.Generic.List[object]
$flaggedFiles = New-Object System.Collections.Generic.HashSet[string]

foreach ($file in $files) {
    $lines = Get-Content -LiteralPath $file.FullName
    $relPath = $file.FullName.Substring($SolutionRoot.Length).TrimStart('\')

    # Name based flag: file name itself suggests a catalog or the Ledger module
    if ($file.Name -match '(?i)(Ledger|ParameterCatalog|ParameterDefinition)') {
        [void]$flaggedFiles.Add($file.FullName)
    }

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($p in $patterns) {
            foreach ($match in [regex]::Matches($line, $p.Regex)) {
                $results.Add([pscustomobject]@{
                    MatchType = $p.Name
                    Value     = $match.Groups[1].Value
                    File      = $relPath
                    Line      = $i + 1
                    Context   = $line.Trim()
                })
                [void]$flaggedFiles.Add($file.FullName)
            }
        }
    }
}

$results |
    Sort-Object MatchType, File, Line |
    Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding UTF8

Write-Host ""
Write-Host "Done. $($results.Count) matches written to $OutputCsv"
Write-Host ""
Write-Host "By match type:"
$results | Group-Object MatchType | Sort-Object Count -Descending | Format-Table Name, Count -AutoSize

Write-Host "Distinct catalog/definition class names found:"
$results |
    Where-Object { $_.MatchType -in @('ClassDeclaration', 'ConstructorCall') } |
    Group-Object Value |
    Select-Object Name, Count |
    Format-Table -AutoSize

Write-Host "$($flaggedFiles.Count) files flagged, dumping full source to $OutputDump ..."

$dumpWriter = New-Object System.Text.StringBuilder
foreach ($fullPath in ($flaggedFiles | Sort-Object)) {
    $relPath = $fullPath.Substring($SolutionRoot.Length).TrimStart('\')
    [void]$dumpWriter.AppendLine("===== $relPath =====")
    [void]$dumpWriter.AppendLine((Get-Content -LiteralPath $fullPath -Raw))
    [void]$dumpWriter.AppendLine("")
    [void]$dumpWriter.AppendLine("")
}

Set-Content -Path $OutputDump -Value $dumpWriter.ToString() -Encoding UTF8

Write-Host "Done. Full source for $($flaggedFiles.Count) files written to $OutputDump"
