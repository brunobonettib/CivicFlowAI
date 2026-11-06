# Tarefa 07 — avaliação do RAG extrativo

Data: 2026-11-05. Endpoint real `POST /api/consultas`; Ollama local, `embeddinggemma:latest` (768 dimensões) e `gemma3:4b`, temperatura 0, TopK 3, três documentos/chunks. Sem downloads, limiar de score ou novas dependências.

## Método e resultado final

Sete casos existentes: dois cobertos, um ambíguo, um parcialmente coberto e três fora do domínio. A avaliação compara status e confere IDs, títulos, URLs, datas e texto literal contra o catálogo/Markdown local. Resultados completos: [rag-results.json](rag-results.json).

| Caso | Esperado | Obtido |
|---|---|---|
| D01: entrar e registrar no Informa.BR | SUPPORTED | UNSUPPORTED |
| D04: prazo LAI e prorrogação | SUPPORTED | SUPPORTED |
| A01: quanto demora meu pedido | NEEDS_CLARIFICATION | UNSUPPORTED |
| A02: procedimento e prazo de recurso | UNSUPPORTED | UNSUPPORTED |
| F01: aposentadoria | UNSUPPORTED | NEEDS_CLARIFICATION |
| F02: prazo de consulta SUS | UNSUPPORTED | NEEDS_CLARIFICATION |
| F03: restituição de imposto | UNSUPPORTED | NEEDS_CLARIFICATION |

- Concordância: **2/7 (28,6%)**. Falsos SUPPORTED: **0/5 casos sem suporte esperado**, nesta execução.
- HTTP 200: 7/7; falhas técnicas: 0. Única resposta SUPPORTED: referências e três passagens literais válidas. Demais respostas: listas de referências vazias.
- D04 informa até 20 dias e prorrogação de 10 dias com justificativa antes do término inicial, usando exclusivamente a ficha MDIC. Não houve transferência desse prazo para SUS ou recurso na resposta final.

Matriz (linhas: esperado; colunas: obtido):

| | SUPPORTED | NEEDS_CLARIFICATION | UNSUPPORTED |
|---|---:|---:|---:|
| SUPPORTED | 1 | 0 | 1 |
| NEEDS_CLARIFICATION | 0 | 0 | 1 |
| UNSUPPORTED | 0 | 3 | 1 |

## Erros encontrados e decisões

A primeira execução, preservada em [rag-results-initial.json](rag-results-initial.json), acertou 2/7 e produziu dois falsos SUPPORTED: A01 e A02. IDs válidos não impediram selecionar informações insuficientes. Foram acrescentados campos estruturados para contexto identificado e lacunas; lacunas declaradas bloqueiam resposta afirmativa, independentemente do status sugerido.

A execução intermediária ([rag-results-coverage.json](rag-results-coverage.json)) acertou 1/7, sem falsos SUPPORTED, mas recusou os dois casos cobertos. Inspeção pontual da saída do modelo em D01 mostrou exigência de prazo e condições não solicitados. A instrução genérica passou a limitar lacunas ao que a pergunta efetivamente exige. Não foram incluídos exemplos, palavras-chave de casos ou regras específicas por pergunta.

Na versão final ainda há recusa indevida de D01 e confusão entre falta de contexto e insuficiência da base. A abordagem é conservadora: respostas afirmativas contêm apenas passagens literais, sem síntese livre. Isso evita inventar texto/URLs, mas **não garante pertinência nem suficiência semântica**. Uma pergunta de esclarecimento também pode ser desnecessária. O MVP está funcional, com qualidade semântica limitada; não é uma conclusão de prontidão para produção.

## Reprodução e próximo passo

Execute a API e `powershell -NoProfile -File evaluation/evaluate-rag.ps1`. O script grava o resultado final e falha por erro técnico ou referência inválida; divergência semântica é métrica, não sucesso artificial. Os modelos são identificados pelos digests locais: Gemma `68ceab7e468a31c988b44d5a4984a0e21f58c4f7158c6ce29c1804babac38df7`; embeddings `85462619ee721b466c5927d109d4cb765861907d5417b9109caebc4e614679f1`.

Os casos foram reutilizados durante o desenvolvimento, portanto não são um conjunto independente. Temperatura zero não garante reprodução bit a bit. Próximo passo: avaliar perguntas inéditas, revisar recusas e esclarecer lacunas documentais, sem perseguir 100% nestes exemplos. Manter geração extrativa até obter evidência de melhoria generalizável.

Validação automatizada: build sem avisos/erros; **196 testes xUnit aprovados** (193 unitários e três integrações reais preexistentes), sem falhas/ignorados; **12/12 cenários HTTP aprovados**. As integrações foram executadas separadamente com `CIVICFLOW_OLLAMA_INTEGRATION=1` e `CIVICFLOW_EVIDENCE_INTEGRATION=1`; a avaliação HTTP acima exercita o pipeline completo. O TRX local está em `%TEMP%/CivicFlowAI-task07/rag-integration.trx`.
