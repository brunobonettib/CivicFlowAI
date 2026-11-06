param([string]$BaseUrl = 'http://localhost:5080', [string]$OutputPath = "$PSScriptRoot/rag-results.json")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$questions = Get-Content (Join-Path $PSScriptRoot 'questions.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$catalog = Get-Content (Join-Path $PSScriptRoot '../data/knowledge/catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$selected = @('D01','D04','A01','A02','F01','F02','F03')
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(180)
$rows = @()
try {
    foreach ($question in $questions | Where-Object { $_.id -in $selected }) {
        $body = @{ pergunta = $question.question } | ConvertTo-Json -Compress
        $content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
        $response = $client.PostAsync("$BaseUrl/api/consultas", $content).GetAwaiter().GetResult()
        try {
            $result = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            $expected = switch ($question.evidence) { 'suficiente' { 'SUPPORTED' }; 'esclarecimento' { 'NEEDS_CLARIFICATION' }; default { 'UNSUPPORTED' } }
            $valid = $true
            if ([int]$response.StatusCode -eq 200 -and $result.status -eq 'SUPPORTED') {
                if ($result.fontes.Count -eq 0 -or $result.trechos.Count -eq 0) { $valid = $false }
                foreach ($source in $result.fontes) {
                    $original = $catalog | Where-Object id -CEQ $source.id
                    if (-not $original -or $original.url -cne $source.url -or $original.titulo -cne $source.titulo -or $original.dataConsulta -cne $source.dataConsulta) { $valid = $false }
                }
                foreach ($excerpt in $result.trechos) {
                    if ($excerpt.fonteId -notin $catalog.id -or $excerpt.fonteId -notin $result.fontes.id) { $valid = $false; continue }
                    $document = (Get-Content (Join-Path $PSScriptRoot "../data/knowledge/$($excerpt.fonteId).md") -Raw -Encoding UTF8).Replace("`r`n", "`n")
                    if (-not $document.Contains($excerpt.texto) -or -not $result.resposta.Contains($excerpt.texto)) { $valid = $false }
                }
            } elseif ([int]$response.StatusCode -eq 200 -and ($result.fontes.Count -ne 0 -or $result.trechos.Count -ne 0)) { $valid = $false }
            $rows += [pscustomobject]@{ id = $question.id; expected = $expected; httpStatus = [int]$response.StatusCode; actual = $result.status; referencesValid = $valid; response = $result }
            Write-Output "$($question.id): HTTP $([int]$response.StatusCode); esperado=$expected; obtido=$($result.status); referências=$valid"
        } finally { $response.Dispose(); $content.Dispose() }
    }
} finally { $client.Dispose() }
$summary = [ordered]@{
    cases = $rows.Count
    correct = @($rows | Where-Object { $_.httpStatus -eq 200 -and $_.actual -eq $_.expected }).Count
    falseSupported = @($rows | Where-Object { $_.actual -eq 'SUPPORTED' -and $_.expected -ne 'SUPPORTED' } | ForEach-Object id)
    technicalFailures = @($rows | Where-Object { $_.httpStatus -ne 200 }).Count
    results = $rows
}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), ($summary | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
if ($summary.technicalFailures -gt 0 -or @($rows | Where-Object { -not $_.referencesValid }).Count -gt 0) { exit 1 }
