# Tarefa 08 — uma tentativa, rejeitada por regressão

2026-11-06; Ollama local, embeddinggemma e gemma3:4b; temperatura zero; TopK 3. Sem novas dependências. **Os prompts de produção foram restaurados ao estado da Tarefa 07. O objetivo de aumentar acertos sem aceitar evidência insuficiente não foi atingido nesta rodada.**

## Diagnóstico

O diagnóstico dos sete casos anteriores reproduziu 2/7 acertos. Todas as consultas enviaram os três documentos ao modelo: recuperação não foi o gargalo. Em D01, a primeira análise foi SUPPORTED; a segunda inventou lacunas (“regras para o prazo de resposta”) que a pergunta não solicitava. Em F01–F03, a primeira análise pediu ao usuário regras que faltavam na base. A01 teve contexto presumido e foi recusado na segunda etapa. A causa predominante é a interpretação semântica inconsistente dos prompts/modelo nas duas etapas; os validadores executam as decisões recebidas e não originam essas recusas.

## Única correção candidata e avaliação

Simplificamos os dois prompts com uma ordem explícita: identificar o objeto; verificar disponibilidade dos fatos; pedir apenas dados indispensáveis do usuário; aceitar cobertura de todas as partes realmente solicitadas. Contratos, recuperação, schema, IDs, fontes, parser e bloqueio por lacunas permaneceram intactos.

Foram avaliados os 15 casos antigos e dez inéditos, rotulados antes da alteração em [rag-unseen.json](rag-unseen.json): cinco cobertos, dois ambíguos e três sem cobertura, incluindo uma pergunta parcialmente coberta. Sem ajustes posteriores de rótulos ou prompt.

| Classe esperada | Acertos / total |
|---|---:|
| SUPPORTED | 7/14 |
| NEEDS_CLARIFICATION | 0/3 |
| UNSUPPORTED | 7/8 |
| Total | 14/25 (56%) |

- Antigos: 10/15; inéditos: 4/10. Nos sete comparáveis: 2/7 → 5/7, mas falsos SUPPORTED passaram de zero para um.
- Falsos SUPPORTED na candidata: **3** — F02 transfere prazos LAI para SUS; N06 e N07 respondem sem objeto identificado.
- Recusas indevidas: N01/N02 (2 UNSUPPORTED em 14 cobertos); esclarecimentos desnecessários: D02/D05/N03/N04 (4/14). Total de negativas semânticas indevidas: 6/14.
- D03: uma falha técnica 502, com lista de aliases repetida e truncada, rejeitada corretamente. Não foi contada como recusa semântica; tampouco como acerto.
- Referências entregues válidas em 24/24 respostas não técnicas. Isso não comprova pertinência: D01/N05 selecionaram também passagens excedentes, apesar do status correto.

## Decisão, reprodução e limite

A candidata foi **revertida**, preservando o comportamento conservador anterior. Não houve segunda correção ou nova rodada de otimização. A tentativa aumentou concordância, mas violou o requisito de não aceitar evidências insuficientes. Não extrapolar o zero anterior para perguntas inéditas: o comportamento restaurado não foi reavaliado nesses dez casos.

Artefatos: [diagnóstico](rag-task08-baseline.json), [resultados da candidata](rag-task08-results.json), [prompts rejeitados](rag-task08-candidate-prompts.json). Registram respostas estruturadas por etapa e hashes dos prompts. Comandos, a partir da raiz: `dotnet run --project evaluation -- rag evaluation/novo-diagnostico.json --baseline` e `dotnet run --project evaluation -- rag evaluation/nova-avaliacao.json`. O segundo usa 15+10 casos com o código atual; reproduzir a candidata exige reaplicar explicitamente os dois prompts arquivados. Não sobrescrever os resultados históricos.

Próximo passo: avaliar a capacidade de um verificador/modelo alternativo de distinguir objeto, lacuna documental e contexto ausente em conjunto reservado, antes de tentar reduzir recusas novamente. Nenhuma garantia semântica decorre da integridade das citações.

Validação após restauração: build sem avisos/erros; **196/196 testes xUnit**, incluindo as três integrações reais, e **12/12 cenários HTTP** aprovados. Nenhum teste existente foi modificado.
