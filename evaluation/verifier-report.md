# Tarefa 09 — comparação offline encerrada

2026-11-06. **Alternativa descartada; endpoint e serviços de produção intactos.**

Alternativa: uma chamada ao mesmo Gemma 3 4B, temperatura zero, avaliando pergunta e trechos recuperados e exigindo citações literais para SUPPORTED. IDs, texto, campos, duplicações e consistência do status são validados estritamente. Baseline: pipeline RAG completo atual, incluindo suas duas etapas. Mesma base, índice e TopK 3; são comparadas as decisões finais, não latência ou custo.

Doze perguntas inéditas, quatro por classe, fixadas antes da execução em [verifier-reserved.json](verifier-reserved.json). Uma rodada, sem ajustes de prompt ou rótulos após resultados. Critério prévio: mais acertos totais, nenhuma classe pior, zero falsos SUPPORTED e zero falhas técnicas.

| Classe esperada | Baseline | Alternativa |
|---|---:|---:|
| SUPPORTED | 0/4 | 3/4 |
| NEEDS_CLARIFICATION | 3/4 | 0/4 |
| UNSUPPORTED | 0/4 | 0/4 |
| Total | 3/12 (25%) | 3/12 (25%) |

- Recusas indevidas em cobertos: baseline 4; alternativa 0, mas houve uma falha técnica no quarto caso coberto.
- Falsos SUPPORTED aceitos: baseline 0; alternativa **4** (R05/R08 ambíguos; R09/R10 sem cobertura). Citações reais não responderam ao limite de anexos nem ao cancelamento de pedidos.
- Falhas técnicas: baseline 0; alternativa **4** (R01/R06/R11/R12), rejeitadas pelo validador. Não contam como recusa semântica nem como acerto. R01 alterou a capitalização de uma citação; não foi normalizada para fazê-la passar.
- O baseline continuou pedindo esclarecimento em vez de reconhecer ausência de cobertura nos quatro casos UNSUPPORTED. Zero falsos SUPPORTED nesta amostra não garante segurança geral.

Reprodução: `dotnet run --project evaluation -- verifier`, a partir da raiz. [verifier-results.json](verifier-results.json) conserva decisões, saídas brutas, aliases/IDs, prompt, hash das perguntas e versão/digests dos modelos. Faça uma cópia antes de reproduzir. Uma falha inicial no gravador (colisão entre nomes JSON) exigiu reiniciar a coleta; apenas o gravador foi corrigido, sem examinar resultados para alterar o verificador.

Build da avaliação sem avisos/erros; 193 testes unitários existentes aprovados. As três integrações preexistentes e os 12 HTTP não foram repetidos: produção não mudou, e a comparação real acima exercitou o pipeline. Nenhum teste existente foi modificado.

Decisão: não integrar. Encerrada a otimização nesta tarefa; o MVP permanece funcional e conservador, com recusas excessivas conhecidas. A amostra pequena não permite generalização; integridade textual não prova suficiência semântica.
