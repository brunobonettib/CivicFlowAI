# Entrega do MVP — 2026-11-06

Preparado para revisão e publicação manual no GitHub. Nenhum push, remoto ou commit foi criado; apenas o repositório local, na branch `main`. O código da API e os testes existentes não foram alterados nesta tarefa. Resultados históricos preservados.

## Validação final única

Ambiente: Windows, SDK .NET 10.0.302, Ollama 0.40.14, modelos locais `embeddinggemma:latest` e `gemma3:4b`. Sem downloads ou novas experiências.

- Build da solução e do executável de avaliação: zero avisos/erros.
- xUnit: **196 aprovados, zero falhas, zero ignorados**. São 193 unitários e três integrações reais habilitadas pelas duas variáveis documentadas no README. A execução recente de 193 excluía essas integrações; não houve redução da suíte.
- Smoke HTTP: **12/12 aprovados**, contra a API com Ollama local.
- Demonstração: **3/3 requisições HTTP 200**, com resultados semânticos abaixo. Ausência de falha técnica não significa classificação correta.
- TRX local: `artifacts/TestResults/final.trx`, excluído do Git.

| Cenário | Esperado | Obtido |
|---|---|---|
| Prazo LAI e prorrogação | SUPPORTED | SUPPORTED |
| “Quanto tempo demora meu pedido?” | NEEDS_CLARIFICATION | UNSUPPORTED |
| Cálculo de aposentadoria | UNSUPPORTED | NEEDS_CLARIFICATION |

A primeira resposta apresentou passagens do MDIC e sua fonte. As outras duas demonstram a limitação persistente: o modelo confunde falta de contexto e ausência de cobertura. Não foram ajustadas para “acertar” a apresentação. [Respostas integrais e instante de execução](demo-results.json).

Reproduza com a API em execução: `powershell -NoProfile -File scripts/demo.ps1 -OutputPath artifacts/minha-demo.json`. Use saída separada para preservar o registro desta entrega. Modelos locais podem variar entre execuções; três cenários não estimam precisão geral.

## Revisão para publicação

O `.gitignore` exclui binários, intermediários, logs, TRX, artefatos temporários, ambientes e formatos comuns de chaves. A listagem de arquivos elegíveis não inclui esses resíduos. Revisão de configuração e busca por padrões comuns não encontraram indícios de credenciais; isso não equivale a uma auditoria completa. Metadados e atribuições do corpus permanecem em `data/knowledge`.

Não há bloqueador técnico reproduzido. Persistem limitações semânticas documentadas, ausência de autenticação/limites de uso e licença do código ainda não definida. O MVP é demonstrável localmente, não está qualificado para decisões oficiais ou exposição como serviço público.

Próximo passo: revisar arquivos e escolher a licença do código; definir repositório/remoto e autorizar publicação. Nenhuma publicação pública foi executada.
