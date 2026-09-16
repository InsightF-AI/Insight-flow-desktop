# Diretrizes de UX/UI do desktop

Referência visual: repositório [`InsightF-AI/Insight-flow-web`](https://github.com/InsightF-AI/Insight-flow-web), commit observado `058b8ed`, em 16/09/2026. O cliente web declara que seus dados são mockados; ele foi usado como referência de interface e fluxo, não como fonte de dados ou contrato de integração.

## Identidade adotada

O desktop segue o conceito de cockpit financeiro do web:

- fundo quase preto (`#070B14`) e superfícies em azul petróleo (`#0F1626`, `#141D33`);
- azul (`#3AA0FF`) para ação e foco, verde (`#22C58A`) para variação positiva, vermelho (`#FB5468`) para negativa, âmbar para sinais e violeta para IA;
- títulos claros, textos auxiliares com contraste reduzido e números tabulares;
- sidebar persistente, busca global no topo, cards para agrupar informação e estados explícitos de carregamento, vazio e erro;
- hierarquia com o dado principal primeiro: ativo, preço, variação e depois contexto/histórico.

O tema escuro é a base inicial. O web também oferece tema claro; essa alternância fica para uma etapa posterior, depois da validação do exemplar escuro e da persistência segura das preferências locais.

## Adaptação entre web e desktop

| Padrão no exemplar web | Aplicação no desktop atual |
|---|---|
| Login dividido entre imagem/mercado e formulário | Painel desenhado localmente com grade e linha de mercado; formulário escuro ao lado |
| Sidebar agrupada em Principal, Alertas e Ferramentas | Mesmos grupos e ordem; módulos sem contrato aparecem desabilitados |
| Busca global na topbar | Busca por ticker/nome usando o endpoint documentado |
| Dashboard com vários cards e dados mockados | Watchlist, cotação e OHLCV reais; cards sem dados reais não são exibidos |
| Card de ativo com preço e variação | Resumo do ativo selecionado, com moeda, mínima, máxima e volume |
| Gráfico e períodos rápidos | Período selecionável e tabela OHLCV nesta etapa; gráfico entra após escolher a biblioteca |
| Feedback de queries e mutations | Mensagens de carregamento, vazio, erro e confirmação junto ao componente afetado |
| Navegação para Portfólio, Alertas e Chat IA | Itens visuais desabilitados até existirem endpoints confirmados |

## Fluxo principal

1. O usuário entra ou cria a conta.
2. A aplicação abre a Visão geral e verifica a disponibilidade da API.
3. A busca global retorna ativos; adicionar à watchlist sempre exige ação explícita.
4. Selecionar um ativo atualiza cotação e histórico, cancelando consultas anteriores.
5. Preferência de notificação e remoção ficam no contexto do item selecionado.
6. Sair limpa token e dados da sessão antes de voltar ao login.

## Regras para as próximas telas

- Cada item da sidebar deve corresponder a um módulo funcional. Não abrir telas vazias ou com resultados inventados.
- A interface pode ser construída antes do backend apenas com fixtures identificadas em ambiente de desenvolvimento; isso não conta como integração concluída.
- Conteúdo de IA usa destaque violeta e sempre mostra o aviso legal obrigatório.
- Portfólio deve declarar que as operações são simuladas e manter valores, moeda e benchmark fornecidos pelo backend.
- Sinais usam âmbar/azul para contexto; verde e vermelho ficam reservados a valores positivos/negativos, sem sugerir compra ou venda.
- A tabela preserva os tipos de data e decimal para permitir ordenação correta; formatação pt-BR ocorre somente na apresentação.
- Cores nunca são o único indicador. Rótulos, sinais e mensagens devem continuar compreensíveis sem distinção cromática.
- Teclado, foco visível, DPI e redimensionamento da janela fazem parte do aceite de cada tela.

## Pendências visuais

- incorporar o ícone oficial quando o uso e o formato do ativo forem confirmados para o executável;
- escolher LiveCharts2 ou ScottPlot e validar candles, volume, zoom e overlays;
- definir comportamento da sidebar recolhível no desktop;
- validar tema claro e persistência da preferência;
- validar a interface em 125%, 150% e 200% de escala do Windows;
- desenhar estados de Portfólio, Alertas, Chat IA e Configurações quando seus contratos estiverem disponíveis.
