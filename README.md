# CivicFlow AI

Prova de conceito de um copiloto para consultas sobre serviços públicos brasileiros, com foco inicial na Lei de Acesso à Informação (LAI).

## Projeto de portfólio

**Problema:** orientações de registro, prazos e acompanhamento estão distribuídas entre páginas oficiais. Uma resposta fluente sem suporte documental pode confundir serviços e procedimentos.

**Solução:** MVP local que recupera fichas de três fontes gov.br, analisa evidências e entrega passagens literais com referências verificadas, esclarecimento ou recusa. Demonstra engenharia de um pipeline RAG, contratos HTTP, testes e avaliação transparente de falhas. **Não é orientação oficial nem um sistema pronto para produção.** A qualidade semântica permanece limitada; experimentos rejeitados estão documentados, não incorporados.

**Stack:** C# / .NET 10, ASP.NET Core Minimal API, Microsoft.Extensions.AI, OllamaSharp 5.5.0, Ollama local (EmbeddingGemma e Gemma 3 4B), xUnit e PowerShell. Sem frontend, banco vetorial, autenticação ou serviços pagos.

**Arquitetura:** Markdown + catálogo → chunks determinísticos → embeddings em memória → pergunta e busca por cosseno TopK → análise de evidências → checagem de cobertura e seleção de passagens → resposta HTTP com fontes locais verificadas. A primeira consulta prepara o índice; as seguintes reutilizam os vetores. A API inicia sem depender do Ollama, mas consultas válidas precisam dos modelos. IDs/JSON válidos não comprovam suficiência da evidência.

Pastas: `src/` API; `data/knowledge/` corpus e atribuições; `tests/` xUnit/smoke; `evaluation/` experimentos offline e resultados históricos; `scripts/` demonstração; `docs/` entrega e registro da demonstração.

**Estado atual: Tarefa 07 — RAG extrativo.** `POST /api/consultas` indexa sob demanda, recupera evidências, verifica cobertura e compõe uma resposta com passagens literais selecionadas pelo modelo e fontes verificadas. Veja a [avaliação do endpoint](evaluation/rag-report.md). Os relatórios das tarefas anteriores permanecem no diretório `evaluation`.

**Tarefa 08:** a única tentativa de reduzir recusas aumentou acertos, mas introduziu falsos SUPPORTED e foi revertida. O comportamento conservador da Tarefa 07 permanece. Veja o [diagnóstico e avaliação de 25 perguntas](evaluation/rag-task08-report.md).

**Tarefa 09:** comparação offline encerrada. A alternativa com citações literais acertou 3/12, igual ao baseline, mas apresentou quatro falsos SUPPORTED; foi descartada. Endpoint preservado. Veja o [relatório reservado](evaluation/verifier-report.md).

## Executar

Pré-requisito: SDK .NET 10.0.302 (ou patch posterior da faixa 10.0.3xx, conforme `global.json`).

Instale o Ollama e mantenha o serviço ativo. Os modelos devem estar instalados localmente; estes downloads são manuais e podem ocupar vários GB:

```powershell
ollama pull embeddinggemma
ollama pull gemma3:4b
ollama list
dotnet restore CivicFlowAI.sln
```

Configure `src/CivicFlowAI.Api/appsettings.json` ou variáveis `Ollama__BaseUrl`, `Ollama__Model`, `Generation__BaseUrl`, `Generation__Model`. Os modelos de embeddings e geração são separados. Não coloque credenciais no repositório.

```powershell
dotnet build CivicFlowAI.sln
dotnet run --project src/CivicFlowAI.Api --no-launch-profile --urls http://localhost:5080
```

Em outro terminal PowerShell:

```powershell
$body = @{ pergunta = 'Como registrar um pedido de acesso à informação?' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/consultas -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
```

Também há requisições de exemplo em `src/CivicFlowAI.Api/CivicFlowAI.Api.http`.

## Demonstração e validação final

Com a API em execução:

```powershell
powershell -NoProfile -File scripts/demo.ps1 -OutputPath artifacts/minha-demo.json
powershell -NoProfile -File tests/http-smoke.ps1
```

A demonstração envia três perguntas reais: prazo LAI, pedido sem contexto e cálculo de aposentadoria. Exibe esperado versus obtido e salva a resposta integral. Não simula respostas, não impõe os rótulos e não transforma divergências semânticas em sucesso de classificação. O [registro da entrega](docs/delivery.md) apresenta a execução final e [suas respostas](docs/demo-results.json).

Na entrega de 2026-11-06: builds limpos, 196/196 xUnit e 12/12 HTTP aprovados. A demonstração retornou SUPPORTED para prazo LAI, UNSUPPORTED para a pergunta ambígua e NEEDS_CLARIFICATION para aposentadoria. Portanto, duas das três classificações da demonstração divergiram do esperado; não foram ocultadas nem corrigidas por regras específicas.

Para executar todos os **196 testes** (193 unitários + duas integrações de embeddings/busca + uma de evidências):

```powershell
$env:CIVICFLOW_OLLAMA_INTEGRATION = '1'
$env:CIVICFLOW_EVIDENCE_INTEGRATION = '1'
try {
    dotnet build CivicFlowAI.sln
    dotnet test CivicFlowAI.sln --no-build
} finally {
    Remove-Item Env:CIVICFLOW_OLLAMA_INTEGRATION, Env:CIVICFLOW_EVIDENCE_INTEGRATION -ErrorAction SilentlyContinue
}
```

Sem essas variáveis, três integrações aparecem como ignoradas; não foram removidas. Os 193 testes recentes excluíam essas categorias. Testes aprovados comprovam contratos e funcionamento, não precisão semântica.

| Avaliação | Resultado observado | Decisão |
|---|---|---|
| RAG atual, sete casos | 2/7 classificações; zero falsos SUPPORTED nessa amostra | Conservado com limitações |
| Tarefa 08, candidata, 25 casos | 14/25; três falsos SUPPORTED | Revertida |
| Tarefa 09, 12 reservados | Baseline e alternativa: 3/12; alternativa com quatro falsos SUPPORTED | Alternativa descartada |

Não comparar essas taxas como se usassem a mesma amostra. Permanecem recusas indevidas, esclarecimentos desnecessários, risco de seleção de texto irrelevante e vulnerabilidade semântica a instruções maliciosas. A base é pequena, manual e pode desatualizar. Sem autenticação, limites de uso ou persistência, o serviço deve permanecer local; referências verificadas não garantem veracidade.

## Publicação

Projeto preparado para revisão e publicação manual no GitHub; não há publicação automática. Binários, resultados temporários de testes, arquivos de ambiente e formatos comuns de chaves são ignorados. Nenhum resultado histórico deve ser substituído por uma execução posterior.

O código próprio, scripts e configuração de projeto/build são licenciados sob [MIT](LICENSE). Corpus, documentação, conjuntos de avaliação, respostas registradas e conteúdo de terceiros ficam fora dessa concessão. Consulte [escopo e atribuições](THIRD_PARTY_NOTICES.md); os direitos das fontes oficiais e das dependências permanecem preservados.

O repositório local está preparado na branch `main`. Antes de configurar publicação, confira `git remote -v`. Após criar ou escolher um repositório GitHub e autorizar o envio, use `git remote add origin <URL_DO_REPOSITORIO>` (somente se não houver `origin`) e `git push -u origin main`. Nenhum desses comandos de publicação é executado automaticamente.

## Contrato

`POST /api/consultas` recebe JSON com `pergunta` obrigatória, não composta apenas por espaços e com no máximo 2000 caracteres.

- `200`: `status` igual a `SUPPORTED`, `NEEDS_CLARIFICATION` ou `UNSUPPORTED`. O primeiro retorna passagens literais com fontes; os demais retornam esclarecimento ou recusa, com listas vazias.
- `400`: pergunta inválida, corpo ausente ou JSON inválido. Erros de validação incluem `errors.pergunta` em Problem Details.
- `415`: tipo de conteúdo incompatível com JSON.
- `502`: saída inválida do provedor, referência inválida ou falha na validação da recuperação.
- `503`: provedor indisponível ou indexação indisponível; `504`: tempo limite; `499`: cancelamento do cliente (a conexão já pode estar encerrada).

O contrato de fonte mantém `id`, `titulo`, `url` e `dataConsulta` (data ISO `yyyy-MM-dd`). Cada trecho mantém `fonteId` e `texto`. Fontes vêm do catálogo local; o modelo não fornece URLs nem texto livre na resposta final.

## Estrutura e limites

Um projeto ASP.NET Core Minimal API. `Program.cs` configura HTTP e dependências; `Contracts/` define entrada e saída; `Knowledge/` contém documentos, chunking e indexação em memória. OllamaSharp 5.5.0 fornece o adaptador de `IEmbeddingGenerator<string, Embedding<float>>` e traz Microsoft.Extensions.AI 10.8.0 como dependência transitiva. Os testes usam xUnit.

## Base local

`data/knowledge/catalog.json` é o catálogo de metadados. Cada ID é único, estável e corresponde a `<id>.md`; deverá acompanhar futuras citações e trechos. Título, órgão, URL original HTTPS gov.br, data de consulta e licença são obrigatórios. `dataAtualizacao` pode ser `null` quando a fonte não a informar. Datas usam `yyyy-MM-dd`; consulta registrada em 2026-11-05 (America/Sao_Paulo).

| ID | Fonte consultada | Atualização informada |
| --- | --- | --- |
| `lai-registro-informabr` | [Catálogo federal / CGU](https://www.gov.br/pt-br/servicos/acessar-informacoes-de-orgaos-publicos) | 2026-07-14 |
| `lai-prazos-mdic` | [MDIC — Prazos](https://www.gov.br/mdic/pt-br/canais_atendimento/ouvidoria/sic/prazos) | 2026-07-01 |
| `lai-acompanhamento-mpo` | [MPO — LAI - Como solicitar Informação](https://www.gov.br/planejamento/pt-br/acesso-a-informacao/servico-de-informacao-ao-cidadao-sic/lai-como-solicitar-informacao) | 2026-08-25 |

As três páginas estavam acessíveis. Os Markdown são fichas factuais autorais, com atribuição, e não transcrições ou versões oficiais. Registram procedimentos e dados, sem reproduzir extensamente a redação das páginas. Os rodapés das fontes indicam [CC BY-ND 3.0](https://creativecommons.org/licenses/by-nd/3.0/): manter atribuição e licença em eventuais reproduções; a licença não autoriza distribuir adaptações do texto original. Não há endosso dos órgãos ao projeto. As notas editoriais estão identificadas.

A página do MPO confirma funcionalidades de acompanhamento, sem detalhar menus; os menus vêm do catálogo federal. A página do MDIC registra particularidades de contagem: não calcular automaticamente o vencimento com uma simples soma de dias. Links para legislação são referências citadas pelas páginas, não documentos adicionais incorporados à base.

O carregador rejeita catálogo ausente/vazio, IDs inválidos/duplicados, metadados inválidos, arquivos ausentes/vazios e Markdown sem cadastro. A validação é estrutural: não comprova veracidade, atualidade ou suficiência da evidência. A base é um retrato manual e precisa ser revisada quando as fontes mudarem.

Os arquivos são copiados para build e publish; o carregamento usa `AppContext.BaseDirectory`, sem depender do diretório de execução. Erros na base impedem a inicialização. Nenhuma requisição externa é feita ao carregar.

## Chunking

`MarkdownChunker` separa seções por títulos ATX (`#` até `######`) e agrupa parágrafos e itens de lista. `Chunking:TargetCharacters`, em `appsettings.json`, define o alvo (padrão: 1000 caracteres UTF-16, incluindo separadores; deve ser positivo). Também pode ser configurado por `Chunking__TargetCharacters` no ambiente.

Parágrafos maiores são separados em fronteiras de frases por heurística conservadora. Frases indivisíveis e itens de lista com suas continuações ficam inteiros, podendo ultrapassar o alvo em um chunk isolado. Não há corte arbitrário de palavras nem sobreposição. Espaços entre unidades são normalizados; o texto e sua ordem são preservados.

Cada `KnowledgeChunk` contém ID, documento, título original, URL, caminho da seção e conteúdo. O ID combina documento, SHA-256 de documento/seção/conteúdo e contador de ocorrências idênticas. É determinístico para a mesma entrada/configuração, inclusive com LF ou CRLF. Alterações de conteúdo, seção ou agrupamento podem mudar os IDs; isso não é um identificador permanente entre revisões.

Os campos `ID:`, `Fonte:` e `Licença indicada na fonte:` são ignorados somente no preâmbulo das fichas. Títulos ficam na seção, não no conteúdo de busca. Notas editoriais do corpo são preservadas. O parser cobre o Markdown simples desta base; não implementa CommonMark completo (tabelas, blocos de código e títulos Setext). A separação de frases não é análise linguística completa.

O serviço e a lista imutável de chunks ficam disponíveis por injeção de dependência e são reutilizados pelo pipeline RAG.

## Embeddings locais

`EmbeddingIndexer.IndexAsync(cancellationToken)` é a operação interna sob demanda; `Current` expõe o último índice completo. O `RagService` prepara o índice na primeira consulta válida, com exclusão mútua, e o reutiliza nas seguintes. Nenhuma conexão ao Ollama ocorre durante a inicialização da API.

Configuração em `appsettings.json`, seção `Ollama`: `BaseUrl` (padrão `http://localhost:11434`), `Model` (`embeddinggemma`), `BatchSize` (2, permitido de 1 a 16) e `TimeoutSeconds` (120). Variáveis como `Ollama__BaseUrl` e `Ollama__Model` sobrescrevem os valores. O tempo limite vale para a geração completa após adquirir a exclusividade da indexação; uma chamada na fila pode ser cancelada pelo chamador.

Os lotes usam apenas o conteúdo do chunk. A associação segue a ordem dos vetores retornados pelo provedor, preservando o chunk completo (ID, documento, título, URL e seção). São rejeitados quantidade incorreta, vetor vazio, dimensões diferentes entre quaisquer lotes e valores NaN/infinito. `truncate=false` evita associar silenciosamente um vetor de texto truncado a um chunk completo.

O índice é substituído atomicamente somente após validar todos os lotes; falhas e cancelamentos preservam o anterior. Chamadas concorrentes são serializadas. O resultado informa `Completed`, `Unavailable`, `Cancelled` ou `InvalidEmbeddings`, sem expor mensagens do provedor. Os vetores são copiados para coleções somente leitura. Não há persistência: reiniciar perde os vetores. Não misture modelos ou configurações de embeddings ao implementar futuras consultas.

### Preparar o Ollama e executar a integração real

Integração real validada com Ollama 0.40.14 e `embeddinggemma:latest`: três documentos, três chunks e vetores de 768 dimensões. Nenhum modelo foi baixado automaticamente pelo projeto.

Instale e abra o [Ollama para Windows](https://ollama.com/download/windows). O [embeddinggemma oficial](https://ollama.com/library/embeddinggemma) requer Ollama 0.11.10 ou posterior e o modelo padrão ocupa aproximadamente 622 MB. Execute o download manualmente:

```powershell
ollama --version
ollama pull embeddinggemma
ollama list
# Se o serviço não estiver ativo, execute em outro terminal:
ollama serve
```

Com o serviço disponível, habilite o teste real (não baixa modelos):

```powershell
$env:CIVICFLOW_OLLAMA_INTEGRATION = '1'
try {
    dotnet test CivicFlowAI.sln --filter 'Category=OllamaIntegration'
} finally {
    Remove-Item Env:CIVICFLOW_OLLAMA_INTEGRATION -ErrorAction SilentlyContinue
}
```

Esse filtro executa as integrações de indexação e busca. Sem a variável, aparecem como **ignoradas**, nunca como integrações aprovadas. Referências técnicas: [interface Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/iembeddinggenerator), [OllamaSharp](https://github.com/awaescher/OllamaSharp) e [API de embeddings do Ollama](https://docs.ollama.com/api/embed).

## Busca semântica

Após executar `EmbeddingIndexer.IndexAsync`, um consumidor interno pode chamar `SemanticSearchService.SearchAsync(pergunta, cancellationToken)`. A busca nunca indexa documentos: usa `Current`, captura uma versão completa do índice e gera somente um embedding para a pergunta, com o modelo registrado nessa versão. Reindexações concorrentes não alteram uma consulta em andamento.

`SemanticSearch:TopK` define quantos resultados retornar (padrão 3, inteiro positivo; variável `SemanticSearch__TopK`). São retornados até TopK chunks, em ordem decrescente de similaridade de cosseno; empates usam o ID em ordem ordinal. Cada resultado contém o `KnowledgeChunk` completo (ID, documento, título, URL, seção, conteúdo) e `Score` entre -1 e 1. O cálculo usa acumulação em double e não exige vetores previamente normalizados.

Perguntas devem conter de 1 a 2000 caracteres, sem apenas espaços. Os estados são `Completed`, `EmptyIndex`, `InvalidQuestion`, `InvalidEmbeddings`, `Unavailable` e `Cancelled`. Quantidade incorreta de embeddings, dimensões incompatíveis, valores não finitos e normas zero impedem a busca inteira, sem resultados parciais. O tempo limite usa `Ollama:TimeoutSeconds`. Falhas de geração não alteram o índice.

**Scores não são probabilidades de acerto.** Não há limiar de relevância. A busca isolada não detecta evidência insuficiente; isso é avaliado nas etapas posteriores. Uma pergunta fora do domínio também pode retornar TopK resultados. A busca é linear no número de chunks; o índice é volátil e o modelo é identificado pelo nome, sem fixação do digest do artefato local.

Na validação real, as perguntas sobre registro, prazo/prorrogação e acompanhamento retornaram respectivamente o catálogo federal, o MDIC e o MPO em primeiro lugar. Isso é uma verificação inicial, não uma garantia geral de relevância.

## Verificação de evidências

Após indexar, o consumidor interno chama `EvidenceAnalysisService.AnalyzeAsync(pergunta, cancellationToken)`. O serviço reutiliza a busca e envia pergunta e chunks identificados ao `IChatClient` do OllamaSharp. Não envia scores ou rótulos de avaliação ao LLM. O prompt trata pergunta, textos e metadados como dados não confiáveis, nunca instruções; solicita cobertura de todas as partes da pergunta usando apenas os trechos fornecidos.

`Generation:BaseUrl`, `Generation:Model` e `Generation:TimeoutSeconds` são separados da configuração de embeddings. O modelo configurado em `appsettings.json` é `gemma3:4b`; `embeddinggemma` permanece exclusivo para embeddings. A construção dos serviços não conecta ao Ollama. O endpoint existente agora utiliza essa análise.

O modelo recebe JSON Schema e deve retornar exatamente:

```json
{
  "status": "NEEDS_CLARIFICATION",
  "evidenceIds": [],
  "justification": "O tipo de pedido não foi informado.",
  "clarificationQuestion": "A qual tipo de pedido você se refere?"
}
```

Estados da análise: `SUPPORTED` exige ao menos uma referência; `NEEDS_CLARIFICATION` exige pergunta de esclarecimento; `UNSUPPORTED` indica ausência ou cobertura parcial das informações necessárias. Nos demais estados a pergunta de esclarecimento deve ser `null`. São rejeitados JSON malformado, campos ausentes/extras/duplicados, status inválidos, texto truncado, referências repetidas/inexistentes ou fora dos chunks recuperados e justificativas acima de 600 caracteres.

No JSON do LLM, `evidenceIds` contém aliases `F1`, `F2`, ... atribuídos na ordem da recuperação e enumerados no schema daquela chamada. O parser faz correspondência exata e converte aliases válidos para os IDs canônicos em `Assessment.EvidenceIds`; `Evidence` conserva os chunks e metadados originais. Aliases não são IDs persistentes: F1 pode representar outro chunk na próxima consulta. Não há normalização, aproximação ou reparação de referências inválidas.

O resultado externo ao JSON do modelo separa `Outcome` operacional de `Assessment`: falhas de comunicação, índice vazio, cancelamento, erro de recuperação e saída inválida não produzem `UNSUPPORTED`; nesses casos não há classificação. O prazo configurado inclui recuperação e geração.

**Limitação:** validar formato e IDs não demonstra que a evidência sustenta a conclusão. Instruções no prompt não garantem resistência a injeção nem veracidade. Não há limiar de similaridade. A [avaliação da recuperação](evaluation/report.md) mostrou sobreposição de scores e continua válida.

### Validar o modelo de geração

Na Tarefa 06, a lista local continha somente `embeddinggemma:latest`. Nenhum modelo de geração foi baixado; a integração real de evidências foi **ignorada**. Os 15 casos de `evaluation/questions.json` foram exercitados com respostas simuladas para validar contratos e associação, não a qualidade do LLM. Expectativas: D01–D09 `SUPPORTED`, A01 `NEEDS_CLARIFICATION`, A02–A03 e F01–F03 `UNSUPPORTED`.

Na Tarefa 06.1, `gemma3:4b` já estava instalado e foi utilizado sem downloads ou alterações do prompt. O [relatório real](evaluation/evidence-report.md) separa erro semântico de rejeição pelo validador. Para reproduzir com o modelo configurado (ou outro já instalado via `Generation__Model`):

```powershell
$env:CIVICFLOW_EVIDENCE_INTEGRATION = '1'
try {
    dotnet test CivicFlowAI.sln --filter 'Category=EvidenceIntegration' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=evidence.trx'
} finally {
    Remove-Item Env:CIVICFLOW_EVIDENCE_INTEGRATION -ErrorAction SilentlyContinue
}
```

A integração usa os 15 casos com busca e LLM reais, registra modelo/digest, status obtidos, IDs e concordância com os rótulos. Falhas técnicas reprovam o teste; divergências de classificação são registradas para análise, sem impor uma taxa arbitrária de aprovação. Um teste aprovado comprova funcionamento estrutural, não qualidade semântica. O relatório TRX contém as métricas, inclusive quando o console não exibe saída de testes aprovados.

## Verificar

```powershell
dotnet test CivicFlowAI.sln
# Com a API em execução:
powershell -NoProfile -File tests/http-smoke.ps1
```

O script preserva os 12 cenários HTTP, atualizando a expectativa de resposta para RAG. A suíte contém 196 testes xUnit: os 174 anteriores e 22 casos novos de orquestração, referências, cobertura, falhas e cancelamento. Testes unitários usam geradores/clientes falsos ou HTTP simulado. Integrações de embeddings e busca usam `CIVICFLOW_OLLAMA_INTEGRATION=1`; a de evidências usa a variável separada acima.

Execução local, sem autenticação ou deploy. RAG pode reduzir respostas sem fundamentação, mas não elimina alucinações.

## Resposta fundamentada

`RagService` orquestra indexação, busca, análise e seleção extrativa. Para `SUPPORTED`, uma segunda chamada independente verifica contexto e lacunas antes de selecionar passagens por aliases `P1`, `P2`, etc. O schema enumera os aliases disponíveis; o parser rejeita IDs inexistentes, repetidos, campos extras, JSON inválido e truncamento. A ausência de contexto pede esclarecimento; lacunas declaradas pelo modelo impedem uma resposta afirmativa, mesmo que o status gerado seja `SUPPORTED`.

Os chunks são conferidos contra a lista canônica e o catálogo. Texto e referências da resposta são montados no servidor, preservando literalmente as passagens e os metadados. Essa escolha conservadora evita afirmações novas na redação, mas não comprova pertinência: o modelo ainda pode selecionar uma passagem inadequada ou recusar cobertura válida. Não há síntese livre, garantia de veracidade ou defesa infalível contra injeção. A base é pequena e contém fichas factuais, não a íntegra das páginas oficiais.

As duas etapas usam temperatura zero. O prazo de `Generation:TimeoutSeconds` também limita a consulta completa, incluindo a preparação inicial do índice. Falhas técnicas retornam Problem Details, nunca são convertidas em ausência de evidências.

Para reproduzir a avaliação enxuta, execute a API e `powershell -NoProfile -File evaluation/evaluate-rag.ps1`. O script grava respostas, referências e métricas em `evaluation/rag-results.json`; não exige um percentual artificial de acerto. Próxima etapa: avaliar perguntas inéditas e melhorar a distinção entre contexto ausente e base insuficiente, antes de ampliar a geração.
