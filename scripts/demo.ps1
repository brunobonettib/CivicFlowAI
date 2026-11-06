param([string]$BaseUrl = 'http://localhost:5080',
      [string]$OutputPath = (Join-Path $PSScriptRoot '../docs/demo-results.json'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$cases = @(
    @{ scenario = 'Fundamentada'; expected = 'SUPPORTED'; question = 'Qual é o prazo de resposta da LAI e em que condições pode ser prorrogado?' },
    @{ scenario = 'Ambígua'; expected = 'NEEDS_CLARIFICATION'; question = 'Quanto tempo demora meu pedido?' },
    @{ scenario = 'Sem cobertura'; expected = 'UNSUPPORTED'; question = 'Tenho 60 anos e 30 anos de contribuição. Quanto vou receber de aposentadoria?' }
)
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(180)
$rows = @()
try {
    foreach ($case in $cases) {
        $body = @{ pergunta = $case.question } | ConvertTo-Json -Compress
        $content = [System.Net.Http.StringContent]::new($body, [Text.Encoding]::UTF8, 'application/json')
        try {
            $response = $client.PostAsync("$($BaseUrl.TrimEnd('/'))/api/consultas", $content).GetAwaiter().GetResult()
            try {
                $payload = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $answer = $payload | ConvertFrom-Json
                $rows += [pscustomobject]@{ scenario = $case.scenario; question = $case.question; expected = $case.expected;
                    http = [int]$response.StatusCode; actual = $answer.status; response = $answer }
                Write-Output "$($case.scenario): HTTP $([int]$response.StatusCode); esperado=$($case.expected); obtido=$($answer.status)"
            } finally { $response.Dispose() }
        } catch {
            $rows += [pscustomobject]@{ scenario = $case.scenario; question = $case.question; expected = $case.expected;
                http = $null; actual = $null; error = $_.Exception.Message }
            Write-Output "$($case.scenario): falha técnica registrada."
        } finally { $content.Dispose() }
    }
} finally { $client.Dispose() }
$path = [IO.Path]::GetFullPath($OutputPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
[IO.File]::WriteAllText($path, (@{ executedAtUtc = [DateTimeOffset]::UtcNow.ToString('o'); results = $rows } |
    ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
if (@($rows | Where-Object { $_.http -ne 200 }).Count -gt 0) { exit 1 }
