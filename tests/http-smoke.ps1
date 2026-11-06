param([string]$BaseUrl = 'http://localhost:5080')

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$client = [System.Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(180)
$cases = @(
    @{ Name = 'Pergunta válida'; Body = '{"pergunta":"Como registrar um pedido de acesso à informação?"}'; Expected = 200 },
    @{ Name = 'Limite de 2000 caracteres'; Body = (@{ pergunta = ('a' * 2000) } | ConvertTo-Json -Compress); Expected = 200 },
    @{ Name = 'Pergunta ausente'; Body = '{}'; Expected = 400; Validation = $true },
    @{ Name = 'Pergunta nula'; Body = '{"pergunta":null}'; Expected = 400; Validation = $true },
    @{ Name = 'Pergunta vazia'; Body = '{"pergunta":""}'; Expected = 400; Validation = $true },
    @{ Name = 'Apenas espaços'; Body = '{"pergunta":"   "}'; Expected = 400; Validation = $true },
    @{ Name = 'Pergunta longa'; Body = (@{ pergunta = ('a' * 2001) } | ConvertTo-Json -Compress); Expected = 400; Validation = $true },
    @{ Name = 'JSON inválido'; Body = '{'; Expected = 400 },
    @{ Name = 'Tipo inválido'; Body = '{"pergunta":123}'; Expected = 400 },
    @{ Name = 'Corpo nulo'; Body = 'null'; Expected = 400 },
    @{ Name = 'Corpo vazio'; Body = ''; Expected = 400 },
    @{ Name = 'Content-Type inválido'; Body = '{}'; Expected = 415; ContentType = 'text/plain' }
)

try {
    foreach ($case in $cases) {
        $contentType = if ($case.ContentType) { $case.ContentType } else { 'application/json' }
        $content = [System.Net.Http.StringContent]::new($case.Body, [System.Text.Encoding]::UTF8, $contentType)
        $response = $client.PostAsync("$($BaseUrl.TrimEnd('/'))/api/consultas", $content).GetAwaiter().GetResult()
        try {
            $payload = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if ([int]$response.StatusCode -ne $case.Expected) {
                throw "$($case.Name): esperado $($case.Expected), recebido $([int]$response.StatusCode)."
            }
            if ($case.Expected -eq 200) {
                $result = $payload | ConvertFrom-Json
                if ($result.status -notin @('SUPPORTED','NEEDS_CLARIFICATION','UNSUPPORTED') -or
                    [string]::IsNullOrWhiteSpace($result.resposta) -or
                    $null -eq $result.fontes -or $null -eq $result.trechos) {
                    throw "$($case.Name): contrato inesperado."
                }
                if ($result.status -eq 'SUPPORTED') {
                    if ($result.fontes.Count -eq 0 -or $result.trechos.Count -eq 0) { throw 'Resposta sem evidências.' }
                    foreach ($trecho in $result.trechos) {
                        if ($trecho.fonteId -notin $result.fontes.id -or -not $result.resposta.Contains($trecho.texto)) { throw 'Referência inconsistente.' }
                    }
                } elseif ($result.fontes.Count -ne 0 -or $result.trechos.Count -ne 0) { throw 'Referências inesperadas.' }
            }
            if ($case.Validation) {
                $problem = $payload | ConvertFrom-Json
                if ($problem.status -ne 400 -or -not $problem.errors.pergunta) {
                    throw "$($case.Name): Problem Details inválido."
                }
            }
        } finally {
            $response.Dispose()
            $content.Dispose()
        }
    }
    Write-Output "HTTP: $($cases.Count)/$($cases.Count) cenários aprovados."
} finally { $client.Dispose() }
