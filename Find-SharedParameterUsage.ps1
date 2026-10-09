<#
.SYNOPSIS
    Scans a Revit add-in C# solution for shared parameter references, by name and by GUID.

.DESCRIPTION
    This is a regex pass over .cs source, not a compiled syntax analysis. It will:
      - Miss any parameter name built dynamically (string.Format, config lookup, constant
        assembled from parts) since there is no literal string to match against.
      - Produce some false positives, mainly from the DefinitionNameCompare pattern, since
        ".Name == "something"" is common outside of parameter code too. Review the Context
        column before treating a row as confirmed.
    Treat the CSV as a starting inventory to review, not a guaranteed exhaustive list.

.PARAMETER SolutionRoot
    Root folder to scan recursively for .cs files.

.PARAMETER OutputCsv
    Path to write the full match list to.

.EXAMPLE
    .\Find-SharedParameterUsage.ps1
    Uses the default BA Tools solution path and writes SharedParameterUsage.csv next to the script.

.EXAMPLE
    .\Find-SharedParameterUsage.ps1 -SolutionRoot "C:\Users\mpocinek\Desktop\Add\BA\BA" -OutputCsv "C:\temp\ba_params.csv"
#>

param(
    [string]$SolutionRoot = "C:\Users\mpocinek\Desktop\Add\BA\BA",
    [string]$OutputCsv = "$PSScriptRoot\SharedParameterUsage.csv"
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

# Each pattern's first capture group holds the value that gets recorded.
# Add or edit patterns here if SharedParameterBindingService's actual method names
# differ from the generic guess below.
$patterns = @(
    @{ Name = 'LookupParameter';        Regex = '\.LookupParameter\(\s*"([^"]+)"\s*\)' }
    @{ Name = 'GetParameterByName';     Regex = '\.get_Parameter\(\s*"([^"]+)"\s*\)' }
    @{ Name = 'BuiltInParameter';       Regex = 'BuiltInParameter\.([A-Za-z0-9_]+)' }
    @{ Name = 'DefinitionNameCompare';  Regex = '\.Name\s*(?:==|\.Equals\()\s*"([^"]+)"' }
    @{ Name = 'SharedParamServiceCall'; Regex = 'SharedParameterBindingService\.\w+\([^)]*"([^"]+)"' }
    @{ Name = 'GuidLiteral';            Regex = '"([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})"' }
)

$results = New-Object System.Collections.Generic.List[object]

foreach ($file in $files) {
    $lines = Get-Content -LiteralPath $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        foreach ($p in $patterns) {
            foreach ($match in [regex]::Matches($line, $p.Regex)) {
                $results.Add([pscustomobject]@{
                    MatchType = $p.Name
                    Value     = $match.Groups[1].Value
                    File      = $file.FullName.Substring($SolutionRoot.Length).TrimStart('\')
                    Line      = $i + 1
                    Context   = $line.Trim()
                })
            }
        }
    }
}

$results |
    Sort-Object MatchType, Value |
    Export-Csv -Path $OutputCsv -NoTypeInformation -Encoding UTF8

Write-Host ""
Write-Host "Done. $($results.Count) matches written to $OutputCsv"
Write-Host ""
Write-Host "By match type:"
$results | Group-Object MatchType | Sort-Object Count -Descending | Format-Table Name, Count -AutoSize

Write-Host "Distinct names, excluding GuidLiteral and BuiltInParameter (review DefinitionNameCompare rows for false positives):"
$results |
    Where-Object { $_.MatchType -notin @('GuidLiteral', 'BuiltInParameter') } |
    Group-Object Value |
    Sort-Object Count -Descending |
    Select-Object Name, Count |
    Format-Table -AutoSize
