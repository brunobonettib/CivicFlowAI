# Tarefa 06.2 — fidelidade de referências

Data de referência: 2026-11-05 (America/Sao_Paulo).

## Causa confirmada antes da correção

Na [execução anterior](evidence-results.json), os IDs completos estavam presentes no JSON enviado ao modelo, inclusive o sufixo `:1`. Nas 14 rejeições, **11 respostas omitiram esse sufixo** e **3 usaram IDs de documentos**, em vez de chunks. Não houve truncamento dos IDs pela aplicação. O schema anterior exigia strings, sem enumerar identificadores permitidos; reproduzir os identificadores extensos era uma fonte de erro do modelo.

## Correção

Cada busca recebe um mapa local determinístico: `F1`, `F2`, ... na ordem dos chunks recuperados. O LLM recebe somente esses aliases como identificadores, junto de título, URL, seção e conteúdo. O JSON Schema enumera exatamente os aliases dessa chamada. Não são enviados IDs canônicos/documentais como alternativas de referência.

O parser exige correspondência ordinal exata, rejeita aliases desconhecidos, malformados ou duplicados e restaura os IDs canônicos e metadados originais na saída interna. Não há trim, comparação aproximada, reparação de sufixo ou escolha de documento para corrigir a resposta. O mapa é recriado por chamada e não vaza para outra consulta. As regras de cobertura, status, ausência de evidência, falha técnica e cancelamento foram mantidas.

## Avaliação real

Gemma `gemma3:4b`, Ollama 0.40.14, embeddings `embeddinggemma:latest`, temperatura **0**, limite de saída **768 tokens**, TopK **3**, **3 chunks / 768 dimensões**. Mesmo conjunto, base e digests de modelos da Tarefa 06.1; apenas o protocolo de referências mudou. Sem ajuste para perguntas específicas. Os [resultados novos](evidence-alias-results.json) preservam aliases enviados/retornados, mapa canônico, respostas brutas e métricas; os resultados antigos não foram sobrescritos.

| Métrica | Antes (06.1) | Depois (06.2) |
| --- | ---: | ---: |
| Saídas estruturalmente válidas | 1/15 | **15/15** |
| Rejeições por referências inválidas | 14/15 | **0/15** |
| Acerto semântico bruto | 7/15 (46,7%) | **10/15 (66,7%)** |
| Classificações corretas aceitas pelo serviço | 0/15 | **10/15** |
| Falsos SUPPORTED brutos | 1 (A02) | **1 (F02)** |
| Falsos SUPPORTED aceitos pelo serviço | 0 (bloqueado por IDs) | **1 (F02)** |

Auditoria independente confirmou mapeamento exato dos aliases para IDs canônicos em **15/15 respostas**. Não houve falhas de comunicação ou de contrato. Isso mede referência estrutural, não sustentação semântica.

Matriz da nova classificação (linhas = esperado, colunas = obtido):

| Esperado / obtido | SUPPORTED | NEEDS_CLARIFICATION | UNSUPPORTED |
| --- | ---: | ---: | ---: |
| SUPPORTED | 9 | 0 | 0 |
| NEEDS_CLARIFICATION | 0 | 1 | 0 |
| UNSUPPORTED | 1 | 4 | 0 |

- **D01–D09:** SUPPORTED, conforme esperado; todos citaram a fonte esperada.
- **A01:** NEEDS_CLARIFICATION, conforme esperado.
- **A02, A03, F01, F03:** NEEDS_CLARIFICATION, mas o esperado era UNSUPPORTED.
- **F02:** SUPPORTED, mas o esperado era UNSUPPORTED.

## Falhas semânticas restantes

**F02 (SUS) é o falso SUPPORTED:** a justificativa atribuiu ao atendimento de saúde os prazos de 20 dias e prorrogação de 10 dias da LAI. Os três aliases eram válidos, mas nenhum trecho sustenta agendamento ou prazo do SUS. Corrigir referências removeu a rejeição técnica, sem resolver essa atribuição indevida.

**A02 (recurso):** nesta execução, o modelo reconheceu que faltavam procedimento e prazo recursal; não repetiu a transferência de 20+10 dias para recurso observada antes. Ainda assim, pediu ao usuário os próprios passos/prazos ausentes, classificando a lacuna documental como necessidade de esclarecimento. O mesmo padrão de pedir contexto para suprir regras ausentes permaneceu em A03/F01/F03.

**D05:** acertou a classe, mas a justificativa descreveu o início da contagem e depois afirmou faltar o momento exato. Acerto de classe não garante justificativa fiel. O modelo novamente não produziu `UNSUPPORTED` em nenhum caso.

## Validação e reprodução

Build da solução e do avaliador sem avisos/erros. **174 testes xUnit passaram**, incluindo todos os cenários anteriores e a integração real antes falha; nenhum ignorado. A execução de integração repetiu **10/15** de concordância e zero falhas técnicas. **12 cenários HTTP passaram**; endpoint inalterado.

Novos testes verificam aliases válidos, inexistentes, fora do TopK, variações de caixa/espaços/sufixo, duplicatas, schema dinâmico, rejeição de IDs canônicos no protocolo do LLM, restauração dos metadados e isolamento do mapa entre chamadas.

```powershell
dotnet run --project evaluation/CivicFlowAI.Evaluation.csproj -- evidence evaluation/evidence-alias-results.json
# Recalcular métricas sem nova geração:
dotnet run --project evaluation/CivicFlowAI.Evaluation.csproj -- evidence-summary evaluation/evidence-alias-results.json
```

As métricas principais são da primeira execução nova registrada no JSON. O arquivo será sobrescrito se o comando for repetido com o mesmo caminho. A repetição pelo teste é evidência adicional, não prova de determinismo.

**Próxima etapa:** avaliar cobertura por parte da pergunta e distinguir lacuna documental de ambiguidade, com revisão humana e conjunto reservado. Não integrar respostas finais enquanto persistir a transferência indevida de prazos. Não relaxar validação, impor limiar de similaridade ou considerar a classificação garantia de veracidade. Esta amostra de 15 casos não demonstra generalização.
