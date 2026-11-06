# Escopo da licença e atribuições

A [licença MIT](LICENSE) cobre somente código próprio: fontes C#, scripts PowerShell e configuração de projeto/build. Inclui a implementação em `src/`, os testes, os scripts e o código dos executáveis de avaliação. Não transfere direitos de terceiros.

Ficam fora dessa concessão MIT o corpus `data/knowledge/`, documentação, conjuntos de perguntas, resultados de avaliações e respostas registradas (inclusive `docs/demo-results.json`). Esta licença não concede direitos adicionais sobre esses materiais. Citações ou outros conteúdos de terceiros, mesmo incorporados em arquivos de código, continuam sujeitos aos direitos dos respectivos titulares.

## Fontes oficiais

As fichas factuais do projeto preservam título, órgão, URL, datas e atribuições no [catálogo](data/knowledge/catalog.json) e em cada Markdown. Não são versões oficiais nem implicam endosso dos órgãos.

- CGU / catálogo federal: [registro de pedidos](https://www.gov.br/pt-br/servicos/acessar-informacoes-de-orgaos-publicos).
- MDIC: [prazos](https://www.gov.br/mdic/pt-br/canais_atendimento/ouvidoria/sic/prazos).
- Ministério do Planejamento e Orçamento: [solicitação e acompanhamento](https://www.gov.br/planejamento/pt-br/acesso-a-informacao/servico-de-informacao-ao-cidadao-sic/lai-como-solicitar-informacao).

As fichas registram a indicação [CC BY-ND 3.0](https://creativecommons.org/licenses/by-nd/3.0/) nas fontes consultadas. O projeto não relicencia o conteúdo oficial como MIT; mantenha as atribuições e consulte as condições da fonte ao reutilizá-lo.

## Dependências e modelos

.NET, pacotes NuGet, Ollama e modelos locais mantêm suas próprias licenças. Bibliotecas compiladas e pesos dos modelos não são distribuídos neste repositório. A licença do código do CivicFlow AI não substitui os termos dessas dependências.
