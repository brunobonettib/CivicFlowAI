# CivicFlow AI — Avaliação da recuperação (Tarefa 05.1)

Data: 2026-11-05, referência America/Sao_Paulo.

## Método

15 perguntas rotuladas **antes** da execução: 9 dentro do domínio (3 por documento), 3 ambíguas/parciais e 3 fora. A expectativa considera as fichas locais, não o conteúdo integral das páginas de origem. Os seis casos sem fonte suficiente não entram no denominador de acerto Top 1; documentos apenas relacionados não os tornam respondíveis.

Busca real com Ollama **0.40.14**, `embeddinggemma:latest`, **3 chunks de 768 dimensões**, alvo de 1000 caracteres e lotes de 2. Índice gerado uma única vez e reutilizado nas 15 buscas. TopK=2 apenas nesta avaliação; a configuração da API permanece 3. Sem geração de respostas, limiar ou ajuste de perguntas após observar scores.

Rótulos e justificativas: [questions.json](questions.json). Resultados completos, digest do modelo e hashes do conjunto/base: [results.json](results.json). Apenas os dois primeiros resultados são registrados; não há vetores nem textos de chunks no arquivo de resultados.

## Resultados

Legenda: **REG** = `lai-registro-informabr`; **PRA** = `lai-prazos-mdic`; **ACO** = `lai-acompanhamento-mpo`. Scores de cosseno arredondados a quatro casas, **não probabilidades**. D = domínio; A = ambígua/parcial; F = fora.

| ID | Pergunta | Fonte suficiente esperada | Top 1 (score) | Top 2 (score) |
| --- | --- | --- | --- | --- |
| D01 | Como entro no Informa.BR e registro um pedido de acesso à informação? | REG | REG (0,7013) | ACO (0,5774) |
| D02 | Preciso pagar alguma taxa para fazer um pedido de informação? | REG | REG (0,5457) | ACO (0,3909) |
| D03 | Depois de enviar o pedido, como recebo o número do protocolo? | REG | REG (0,3724) | PRA (0,3603) |
| D04 | Qual é o prazo de resposta da LAI e em que condições pode ser prorrogado? | PRA | PRA (0,6309) | ACO (0,4806) |
| D05 | Se envio meu pedido de informação às 20h, quando começa a contagem do prazo? | PRA | PRA (0,6099) | REG (0,4455) |
| D06 | O que acontece se o prazo do pedido de informação vencer em um dia sem expediente? | PRA | PRA (0,5678) | REG (0,4036) |
| D07 | Consigo pesquisar pedidos e respostas públicos por ano, órgão e instância de recurso? | ACO | ACO (0,5498) | REG (0,5086) |
| D08 | Desde quando o Busca LAI reúne pedidos públicos e ele mostra informações pessoais ou sigilosas? | ACO | ACO (0,6201) | REG (0,4860) |
| D09 | No Informa.BR posso acompanhar o tratamento do pedido, receber a resposta do órgão e apresentar recurso? | ACO | **REG (0,6783)** | ACO (0,6531) |
| A01 | Quanto tempo demora meu pedido? | Nenhuma: esclarecer o tipo de pedido | PRA (0,4294) | REG (0,2916) |
| A02 | Como faço para recorrer de uma resposta ao pedido de informação e qual é o prazo do recurso? | Nenhuma: ACO é apenas parcial | REG (0,4755) | PRA (0,4731) |
| A03 | Meu pedido de informação foi negado por sigilo. A negativa foi correta? | Nenhuma: faltam fatos e regras | REG (0,4786) | ACO (0,4206) |
| F01 | Tenho 60 anos e 30 anos de contribuição. Quanto vou receber de aposentadoria? | Nenhuma | REG (0,2916) | PRA (0,2860) |
| F02 | Como agendar uma consulta pelo SUS e qual é o prazo de atendimento? | Nenhuma | PRA (0,5073) | REG (0,4147) |
| F03 | Como consultar o andamento da restituição do imposto de renda e quando vou receber? | Nenhuma | PRA (0,4274) | REG (0,4009) |

**Acerto Top 1: 8/9 = 88,9%.** Por documento esperado: REG 3/3; PRA 3/3; ACO 2/3. A fonte esperada apareceu no Top 2 em 9/9 casos. Falhas operacionais: **0/15**.

Distribuição do **score Top 1**, sem misturar os casos ambíguos ao grupo fora do domínio:

| Grupo | n | Mínimo | Mediana | Média | Máximo |
| --- | ---: | ---: | ---: | ---: | ---: |
| Domínio | 9 | 0,3724 | 0,6099 | 0,5862 | 0,7013 |
| Ambígua/parcial | 3 | 0,4294 | 0,4755 | 0,4612 | 0,4786 |
| Fora | 3 | 0,2916 | 0,4274 | 0,4088 | 0,5073 |

## Falhas e recomendação

- **D09:** REG venceu com score alto, mas não cobre o recurso; ACO, que cobre as três funcionalidades, ficou em segundo. O maior score não garante evidência suficiente.
- **A02:** nenhum resultado contém procedimento e prazo recursal. O prazo de resposta da LAI em PRA não pode ser reutilizado como prazo de recurso. A01 exige esclarecimento; A03 não permite julgar uma negativa concreta.
- **F02/F03:** recuperaram PRA, embora saúde e restituição tributária não sejam cobertas. Uma pergunta fora do domínio recebe resultados mesmo sem evidência pertinente.

**Não propor limiar único com esta amostra.** Os intervalos de domínio e fora se sobrepõem entre 0,3724 e 0,5073. Um corte que preserve D03 também aceita F02/F03 e os três casos ambíguos; um corte que exclua todos os casos fora rejeita D03. D09 continuaria sendo uma ordenação inadequada mesmo com score elevado. Estes são contrastes observados, não limiares recomendados.

Na etapa de geração, verificar explicitamente se os trechos sustentam **cada parte** da pergunta e da resposta, com citações correspondentes; pedir esclarecimento ou recusar a parte sem suporte. Essa verificação também precisará ser avaliada, não assumida confiável. Ampliar perguntas e reservar um conjunto independente antes de calibrar qualquer regra: 15 casos, 3 documentos e uma execução não estimam qualidade geral nem garantem ausência de alucinações.

## Reprodução e validação

Na raiz, com Ollama e modelo já instalados:

```powershell
dotnet run --project evaluation/CivicFlowAI.Evaluation.csproj -- evaluation/results.json
```

O comando reutiliza os componentes da API, sem dependências novas; sobrescreve somente o JSON indicado. O relatório é a análise desta execução e deve ser revisto ao repetir com outros dados/modelos.

Build da solução e do avaliador sem avisos/erros. **107 testes xUnit aprovados**, incluindo as duas integrações reais; nenhum ignorado. **12 cenários HTTP aprovados**. Código da API e testes existentes preservados.
