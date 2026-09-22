# Plano de implementação e pendências

Proposta baseada nos documentos recebidos, em 16/09/2026. Os marcos abaixo organizam o trabalho do desktop; não reduzem o MVP completo descrito no PDF.

## 1. Estado encontrado

- Repositório `InsightF-AI/Insight-flow-desktop` acessível e clonado, inicialmente sem commits ou código.
- Desktop definido como Windows Forms/C#. Versão de .NET não fixada nas fontes; .NET 10 LTS é a proposta deste guia.
- Referência da API datada de 15/09/2026, vinculada à branch `feature/alerta-service`, sem contrato versionado.
- Nenhum backend iniciado, endpoint executado ou integração validada nesta análise. Comandos contidos nos anexos foram lidos como referência.
- Orientações criadas localmente; nenhuma alteração publicada no GitHub.

Após essa análise, foi iniciada a estrutura executável descrita no [README](../README.md): solução .NET 10, cliente HTTP, sessão, telas do primeiro fluxo e testes com respostas controladas. A disponibilidade do backend da equipe continua sem validação; os marcos abaixo não devem ser considerados integralmente concluídos só pela existência de código local.

### Situação da estrutura local em 16/09/2026

| Área | Situação local | Limite atual |
|---|---|---|
| Base, autenticação e sessão | Implementada e coberta por testes de contrato | Falta executar contra uma instância real da API |
| Busca, watchlist, cotação e histórico | Implementados na interface | Falta validar dados, latência e erros do backend da equipe |
| Indicadores, sinais e backtest | Implementados em abas no detalhe do ativo | Unidade dos retornos e schemas auxiliares ainda precisam de confirmação |
| Alertas de preço | CRUD inicial implementado, incluindo PATCH parcial e exclusão 204 | Avaliação e entrega de notificações dependem do backend |
| Portfólio, IA e notificações nativas | Planejados | Não há contrato suficiente na referência recebida |

Com isso, a estrutura inicial local fica fechada para integração: os 16 endpoints de recurso e o health check descritos na referência possuem representação no cliente, e os fluxos com interface usam dados da API sem fixtures no código de produção.

## 2. Sequência de entregas

| Marco | Trabalho | Critério de aceite |
|---|---|---|
| 0. Alinhamento | Confirmar URL, versão/commit, contrato OpenAPI ou equivalente, conta de teste e convenções do repo | Ambiente de integração identificado; limitações anotadas; ferramenta de gráficos e framework compatíveis |
| 1. Base e sessão | Criar solução, cliente HTTP, configuração e telas de cadastro/login | Compila e abre no Windows; cadastro autentica; login inválido e sessão expirada recebem tratamento correto; logout limpa a sessão |
| 2. Primeiro fluxo completo | Buscar ativo, adicionar explicitamente à watchlist, listar e abrir cotação/histórico | Um ativo percorre todo o fluxo com a API real; sem travar a UI; IDs e erros tratados corretamente |
| 3. Análise técnica | Indicadores recentes, sinais, backtest e preferência de notificação | Valores coincidem com respostas da API; sinal ativo/desativado distinguível; retornos nulos não aparecem como zero |
| 4. Alertas de preço | Criar, listar, editar, habilitar/desabilitar e excluir | PATCH usa os campos corretos; valor-alvo inválido não é enviado; exclusão 204 funciona; listas respeitam o usuário |
| 5. Notificações | Integrar contrato de eventos ao Windows e à bandeja | Uma ocorrência gera uma notificação; reconexão e duplicação têm comportamento definido; logout interrompe recebimento |
| 6. Portfólio e IA | Operações simuladas, agregados, análise textual, resumo e chat | Integração validada contra novos contratos; valores do backend; avisos presentes; indisponibilidade parcial tratada |
| 7. Entrega acadêmica | Revisão de UX, testes dos fluxos e empacotamento | Aplicação distribuível testada no Windows de destino; README de execução; limitações e demonstração documentadas |

Os marcos 5 e 6 dependem de contratos ainda não apresentados. Layouts e componentes podem ser preparados com fixtures identificadas durante a espera; a entrega só é integrada após validação real.

O PDF §7 reserva duas semanas para desktop e notificações dentro de um cronograma geral. Essa é uma estimativa do planejamento original, baseada em API existente; não é prazo confirmado para o estado atual. Reestimar após alinhar dependências e data de apresentação.

## 3. Divergências e decisões com a equipe

| Prioridade | Tema e evidência | Encaminhamento |
|---|---|---|
| Antes de integrar | HTML representa uma branch, sem garantia de deploy | Obter URL de homologação/desenvolvimento e identificar commit/esquema compatível |
| Antes de abrir detalhes pela busca | Busca exemplifica ticker sem UUID; consultas requerem `ativo_id` | Pedir ID na busca ou contrato de resolução. Até lá, detalhes por `ativo.id` da watchlist, após inclusão intencional |
| Sessão | DOCX RNF-04 exige refresh; HTML §3 informa que não existe | Implementar novo login no contrato atual e manter refresh como requisito pendente do backend |
| Sessão | UC-01 menciona recuperação de senha; HTML não descreve rota | Confirmar fluxo e contrato antes de disponibilizar ação funcional |
| Análise técnica | RF-08/RN-03 exigem contexto de 24 meses; HTML não explicita janela consultada nem unidade dos retornos | Confirmar período e unidade antes de rotular percentuais ou afirmar cobertura de 24 meses |
| Análise técnica | PDF §2.1 menciona taxa de acerto; HTML fornece quantidade de ocorrências e retornos médios | Definir métrica/contrato de taxa de acerto; retorno médio não é probabilidade de sucesso |
| Análise técnica | Score composto consta no PDF e no serviço de sinais do DOCX, sem campo/rota no HTML | Manter pendente; não recalcular score no desktop |
| Gráficos | HTML exemplifica indicadores recentes, com objetos auxiliares sem schema detalhado | Confirmar parâmetros efetivos, campos auxiliares e disponibilidade de séries históricas para sobreposição |
| Notificações | PDF §2.2 inclui ativação/desativação; DOCX RF-09/UC-05 enfatiza nova ativação | Definir quais mudanças geram evento e como apresentar cada uma |
| Notificações | RF-09/UC-05 pede entrega e fila offline; HTML só documenta preferências e CRUD de alertas | Definir transporte, identidade de eventos, reconexão, confirmação de leitura/entrega se aplicável e execução em bandeja |
| Portfólio/IA | RF-10 a RF-15 previstos; rotas ausentes no HTML | Solicitar contratos e sequência de disponibilização; manter no MVP completo |
| Dividendos | PDF §2.3 inclui histórico; RFs/modelo do DOCX e HTML não o especificam | Confirmar permanência no MVP, dados de origem, modelo e endpoints |
| Fallback de IA | RNF-02 prevê análise pré-gerada; UC-03 permite apenas dados numéricos | Definir resposta e indicação de conteúdo anterior/ausente; cliente deve aceitar estados explícitos sem inventar análise |
| Resumo diário | RF-14 prevê primeira notificação do dia seguinte, sem horário/fuso definidos | Alinhar agendamento, fuso do usuário e ordenação da entrega |
| Portfólio em várias moedas | RF-12 inclui moeda; descrição de `PortfolioService.distribuicao` só cita classe/setor | Confirmar agrupamentos e moeda-base dos agregados antes de apresentar totais |
| Privacidade de análises | RN-09 vincula análises ao usuário; entidade `AnaliseIA` não lista `usuario_id` | Definir separação entre análise do ativo e conteúdo privado da carteira e verificar autorização no backend |
| Criptomoedas | PDF/DOCX preveem Binance; HTML descreve busca/cotações via brapi apesar do enum `CRIPTO` | Confirmar ativos/pares e rotas suportados; não usar BTC como primeiro teste só por constar no roadmap original |
| Backtest de cripto | RF-08 usa janelas em pregões para um produto que inclui cripto | Definir no backend o calendário e a interpretação dessas janelas |
| Dados de mercado | Contrato omite precisão decimal, ordem/granularidade, datas de pregão e atualização | Acordar os formatos antes de consolidar DTOs e gráficos |
| Qualidade | RNF-09 exige 60% nos services; definição no DOCX enfatiza backend | Alinhar aplicação ao desktop e critérios de avaliação da disciplina |

Para notificações e agendamento, RN-06 especifica reavaliação máxima de 15 minutos no pregão para renda variável e 5 minutos para cripto. São responsabilidades do backend, não intervalos de polling automaticamente autorizados para cada cliente.

## 4. Validação a executar durante a implementação

### Primeiro fluxo integrado

1. Apontar a aplicação para um ambiente autorizado e conferir a identificação da API.
2. Cadastrar uma conta de teste ou autenticar com uma fornecida pela equipe.
3. Pesquisar e adicionar intencionalmente um ativo à watchlist; obter seu UUID pela resposta.
4. Exibir cotação e histórico, trocar períodos e tratar histórico vazio/indisponível.
5. Consultar indicadores, sinais e backtest preservando campos nulos.
6. Alterar a preferência de notificação, remover o ativo e verificar os estados de sucesso/erro.
7. Sair, entrar com outro usuário e confirmar ausência de dados remanescentes da sessão anterior.

O HTML indica `PETR4`, `MGLU3`, `VALE3` e `ITUB4` para cotação/histórico sem chave brapi no backend. Confirmar essa disponibilidade no ambiente usado; ela não foi testada nesta análise.

### Casos de falha relevantes

- Login incorreto, token expirado, ausência de autorização e acesso a recurso de outro usuário.
- E-mail ou watchlist duplicados (`409`); validação de formulário (`422`) nos dois formatos de erro.
- Fonte de mercado indisponível (`503`), timeout, conexão perdida e resposta inesperada.
- Exclusão com `204`, sem desserializar JSON vazio.
- Troca rápida de ativo/tela e logout com consulta ainda em andamento.
- Atualização de alerta usando `ativo` no request e `habilitado` na resposta.
- Histórico insuficiente, backtest nulo e indicadores com campos auxiliares ausentes/nulos.
- Formatação decimal, moedas, datas, escala de DPI e navegação por teclado.

Testes automatizados devem cobrir parsing, sessão e coordenação dos fluxos; testes de integração exigem ambiente e dados controlados. Cobrir a UI manualmente onde a automação não trouxer benefício proporcional.

## 5. Trabalho no GitHub

Proposta: implementar cada marco em uma branch curta, com descrição do comportamento entregue e evidência de validação. Alinhar com a organização a branch padrão, revisão de PRs e convenções antes do primeiro envio. O repositório não tinha histórico para inferir esses padrões.

Não versionar senhas, tokens, arquivos de configuração com segredos, logs pessoais ou resultados de build. Ao criar a solução, adicionar um `.gitignore` apropriado e configuração de exemplo sem credenciais. Publicação de commits e abertura de PRs não ocorreram nesta etapa de documentação.

## Evolução local em 20/09/2026

- Gráfico nativo de fechamento e volume, ligado ao mesmo histórico da tabela OHLCV, com consulta por mouse/setas e suporte a séries vazias, constantes e de um registro.
- Cartão de cotação com altura automática para ampliar a área de análise. Datas do gráfico preservam o fuso recebido; o eixo horizontal usa registros igualmente espaçados, sem deduzir frequência de pregão.
- Backtests cancelados ou ultrapassados por outra seleção não alteram o estado atual da tela.
- Criação/edição de alerta atualiza a lista com a resposta da própria operação; exclusão remove o item confirmado. Uma segunda consulta não pode transformar um salvamento bem-sucedido em mensagem de falha. Atualização manual continua disponível.
- O preço-alvo preserva a faixa decimal do contrato, sem limitar silenciosamente valores carregados a um bilhão.
- A configuração encontrada continua em localhost:8000, sem URL alternativa no ambiente; o health check local não respondeu. Validação com backend real permanece pendente.

Próximo aceite: executar cadastro/login, watchlist, períodos, análise técnica e alertas contra a versão da API da equipe. Depois, validar escala do Windows e distribuição; candles, zoom e séries históricas de indicadores são evoluções do gráfico inicial.