# Tarefa 06.1 — Gemma 3 4B: avaliação real de evidências

Data de referência: 2026-11-05 (America/Sao_Paulo).

## Execução

Ollama **0.40.14**, geração **gemma3:4b** e embeddings **embeddinggemma:latest**, ambos já instalados. Temperatura **0**, máximo de **768 tokens** de saída, TopK **3**, índice único com **3 chunks / 768 dimensões**. Nenhum download. O prompt e o `EvidenceAnalysisService` permaneceram inalterados; nenhum ajuste foi feito a partir dos resultados.

Os [15 casos e rótulos anteriores](questions.json) foram mantidos. O modelo recebeu somente a pergunta e os chunks realmente recuperados, sem rótulos esperados. [Resultados auditáveis](evidence-results.json) registram respostas brutas, IDs enviados/retornados, resultado validado, digests dos modelos e hashes do prompt, base e perguntas. As métricas abaixo se referem à primeira execução do avaliador, sem escolher a melhor tentativa.

## Métricas

- **Classificação bruta do LLM:** 7/15 corretas (**46,7%**), desconsiderando a validade dos IDs apenas para diagnóstico.
- **Pipeline completo:** 0/15 saídas corretas aceitas (**0%**). Uma saída passou pela validação (D07), mas errou a classe; **14/15 retornaram `InvalidResponse`** por IDs incorretos. Não houve indisponibilidade/timeout nesta execução.
- **Falsos `SUPPORTED` brutos:** **1**, A02; 1/6 casos cujo esperado não era `SUPPORTED` (16,7%). O serviço bloqueou essa saída por IDs inválidos; **nenhum falso `SUPPORTED` foi aceito**. Isso não demonstra segurança semântica: corrigir só os IDs exporia o erro.
- **IDs:** apenas **1/15 respostas** citou exclusivamente IDs exatos dos chunks recuperados. Em **11 casos**, o LLM omitiu o sufixo `:1`; em **3**, usou IDs de documentos (D04, F02, F03). Não houve reparação automática das referências.

Matriz de confusão **da classificação bruta**, inclusive saídas rejeitadas pelo serviço. Linhas = esperado; colunas = produzido pelo LLM:

| Esperado / produzido | SUPPORTED | NEEDS_CLARIFICATION | UNSUPPORTED |
| --- | ---: | ---: | ---: |
| SUPPORTED | 6 | 3 | 0 |
| NEEDS_CLARIFICATION | 0 | 1 | 0 |
| UNSUPPORTED | 1 | 4 | 0 |

O modelo **nunca produziu `UNSUPPORTED`**. Na matriz das saídas aceitas há apenas uma ocorrência: esperado `SUPPORTED` → `NEEDS_CLARIFICATION`; as 14 rejeições são falhas de contrato, não classificações `UNSUPPORTED`.

| Casos | Esperado | LLM bruto | Serviço |
| --- | --- | --- | --- |
| D01, D02, D04, D05, D06, D09 | SUPPORTED | SUPPORTED | InvalidResponse |
| D03, D08 | SUPPORTED | NEEDS_CLARIFICATION | InvalidResponse |
| D07 | SUPPORTED | NEEDS_CLARIFICATION | Completed |
| A01 | NEEDS_CLARIFICATION | NEEDS_CLARIFICATION | InvalidResponse |
| A02 | UNSUPPORTED | SUPPORTED | InvalidResponse |
| A03, F01, F02, F03 | UNSUPPORTED | NEEDS_CLARIFICATION | InvalidResponse |

## Erros relevantes

1. **A02 — recurso:** o modelo afirmou que o prazo recursal seria de 20 dias, prorrogável por 10, e que a ficha do MPO detalharia o procedimento. Os chunks contêm esses prazos para **resposta ao pedido**, e apenas mencionam a possibilidade de recurso; não sustentam procedimento/prazo recursal. É um falso `SUPPORTED`, apesar da instrução explícita para evitar essa confusão.
2. **D04 — justificativa:** mesmo acertando a classe bruta, o modelo atribuiu a justificativa da prorrogação ao solicitante. A ficha descreve justificativa que deve chegar ao solicitante. Acertar a classe não assegura fidelidade da justificativa.
3. **D03/D07/D08 — esclarecimento desnecessário:** conteúdo coberto foi tratado como ambíguo. D03 reconhece o protocolo por e-mail e, contraditoriamente, afirma faltar como recebê-lo. D07 exige esclarecimento sobre filtros já descritos na base.
4. **A03/F01/F02/F03 — lacuna documental:** ausência de regras sobre sigilo concreto, aposentadoria, SUS e restituição foi transformada em pedidos de esclarecimento. Mais contexto do usuário não acrescenta essas regras aos documentos disponíveis.

## Recomendação

**Não integrar resposta final ainda.** Corrigir primeiro a fidelidade das referências com identificadores curtos restritos por schema e mapeamento determinístico para IDs estáveis, mantendo a rejeição de referências inválidas. Separadamente, avaliar cobertura de cada parte da pergunta e distinguir ambiguidade de ausência de informação. Não basta corrigir IDs ou aceitar a maior similaridade.

Reavaliar em perguntas reservadas e com revisão humana dos trechos; não ajustar o prompt apenas para estes 15 exemplos. Uma execução pequena, mesmo com temperatura zero, não garante consistência, veracidade ou resistência a injeção. Não foi definido limiar de score.

## Reprodução

Na raiz, com os modelos instalados e a configuração atual:

```powershell
dotnet run --project evaluation/CivicFlowAI.Evaluation.csproj -- evidence evaluation/evidence-results.json
```

O avaliador retorna código 1 se houver falhas técnicas/contratuais, preservando o JSON. Para recalcular as métricas da mesma execução, **sem chamar o modelo**:

```powershell
dotnet run --project evaluation/CivicFlowAI.Evaluation.csproj -- evidence-summary evaluation/evidence-results.json
```

Build da solução e do avaliador sem avisos/erros. Dos **164 testes xUnit executados**, **163 passaram** e **1 falhou**: a integração real de evidências esperava zero falhas de contrato e encontrou **14** em sua execução separada. Os **12 cenários HTTP passaram**. Nenhum teste foi ignorado ou afrouxado para aceitar IDs inválidos ou classificações incorretas. As métricas principais acima permanecem as da primeira execução registrada no JSON; a repetição pelo teste confirmou as 14 rejeições.
