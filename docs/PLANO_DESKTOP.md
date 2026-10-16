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
| Sessão | DOCX RNF-04 exige refresh; HTML §3 informa que não existe | Resolvido em 08/10/2026: o backend passou a ter refresh e logout, e o desktop renova a sessão sozinho |
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

## Evolução local em 30/09/2026

Contrato conferido no código da branch `develop` do backend (commit `a175cb6`), sem alterar o backend. Detalhes em [Integração com a API](INTEGRACAO_API.md#atualização-de-30092026-código-do-backend-conferido).

- **Correção:** `ultimo_estado` do alerta passou a ser lido como booleano. A lista de alertas mostra "Monitorando", "Condição atingida" ou "Desabilitado".
- **Notificações (marco 5, parcial):** tela com filtro de não lidas, detalhes (incluindo o texto do resumo diário e o aviso legal) e ação de marcar como lida. A janela principal consulta as não lidas a cada 2 minutos, mostra o contador no menu e avisa pela bandeja do Windows. A primeira consulta da sessão gera um único aviso resumido; logout interrompe a consulta. Não há transporte em tempo real no backend.
- **Portfólio (marco 6):** registro e exclusão de operações de compra/venda, posições, rentabilidade em BRL, comparação com CDI/Ibovespa e distribuição por classe, setor e moeda. Cada seção trata indisponibilidade de forma independente. Percentuais do backend são frações e são exibidos multiplicados por 100.
- **IA (marco 6):** aba "Análise IA" no detalhe do ativo, gerada sob demanda para não consumir cota a cada seleção, e tela de Chat IA com histórico limitado a 20 mensagens. O aviso legal retornado pela API é sempre exibido. Bloqueio por guardrail (502), cota excedida e IA desabilitada (503) têm mensagens próprias; uma mensagem que falha não entra no histórico e volta para o campo de texto.
- **Backtest:** retornos exibidos em %, conforme o cálculo confirmado no backend.

| Área | Situação local | Limite atual |
|---|---|---|
| Notificações | Caixa, leitura e aviso na bandeja implementados | Consulta periódica; entrega com o app fechado e fila offline dependem de novo contrato |
| Portfólio | Operações, posições, rentabilidade, benchmark e distribuição implementados | Posições não informam moeda: o desktop usa a moeda do ativo da watchlist quando disponível |
| IA | Análise por ativo e chat implementados | Depende de `AI_HABILITADA` e da chave do provedor no servidor |

Testes de contrato foram adicionados para as 11 rotas novas e para o `ultimo_estado` booleano. Nesta máquina, a compilação passou sem erros nem avisos, mas o Smart App Control do Windows bloqueou o carregamento das DLLs recém-compiladas e impediu a execução dos testes. Numa execução posterior no mesmo dia, `dotnet test` rodou nesta máquina: 40 testes aprovados, nenhuma falha. 

### Validação com a API real (30/09/2026)

Backend `develop` (commit `a175cb6`) executado localmente com `docker compose up`: Postgres, Redis, API e todas as migrations. O cliente HTTP do desktop foi exercitado contra essa instância, sem fixtures. Houve 37 verificações, todas com o resultado esperado:

- Autenticação: login inválido (401), cadastro inválido (422 com mensagens por campo), e-mail duplicado (409), cadastro, login, token inválido e requisição sem sessão (401).
- Watchlist: busca, inclusão, duplicidade (409), listagem, preferência de notificação e remoção (204).
- Mercado: cotação e histórico `1D`/`1S`/`1M`/`3M` para PETR4 e VALE3 (162, 4, 22 e 64 pontos), e indicadores.
- Alertas: criação, listagem, PATCH com `valor_alvo` e `ativo`, e exclusão (204). O preço-alvo zero é recusado pelo cliente antes do envio.
- Notificações: consulta de não lidas.
- Portfólio: compra, operações, posições, rentabilidade, distribuição e exclusão (204). Benchmark CDI e Ibovespa voltam com `rentabilidade_benchmark_percentual` nulo nesse ambiente, e a tela mostra "indisponível no momento".
- IA: 503 "Provedor de IA indisponivel", porque o compose não habilita a IA.
- Isolamento: um segundo usuário não vê watchlist, alertas nem operações do primeiro.

Não validado:
- Sinais: a lista voltou vazia, pois depende do agendador gerar sinais, e sem sinal não há como testar o backtest.
- Notificação na bandeja: nenhuma notificação foi gerada.
- IA com provedor configurado.

Pendência encontrada: uma venda maior que a posição, ou a exclusão de uma compra que deixa uma venda posterior descoberta, retorna 422 com apenas o UUID do ativo em `detail`. A tela de portfólio exibiria esse UUID como mensagem de erro. Há uma correção no desktop que troca o UUID por uma mensagem explicativa, aguardando revisão. Sugerir ao backend uma mensagem legível para `QuantidadeInsuficienteError`.
## Revisão geral em 01/10/2026

A correção do 422 de quantidade insuficiente, citada acima, entrou em `develop` e `main` (PR #1). O contrato foi reconferido no backend (`develop` e `main` no commit `a175cb6`, sem diferenças) e o backend não foi alterado. Esta revisão percorreu todo o código do desktop; o que mudou:

- **Escala do Windows:** a escala de DPI era configurada antes de os controles existirem e acabava aplicada só às fontes. Em 125%, textos da barra lateral, cabeçalhos e colunas ficavam cortados e o gráfico não cabia na janela. `AsyncForm` passou a aplicar a escala depois de a tela montar seus controles, e a janela não excede mais a área útil do monitor.
- **Ordenação das tabelas:** datas, quantidades, valores e percentuais eram gravados como texto, e clicar no título da coluna ordenava em ordem alfabética (01/10 antes de 20/08; "BRL 1.300,00" antes de "BRL 400,00"). As células agora guardam o valor tipado.
- **Layout:** botões da barra lateral com a largura correta, cartão de cotação compacto, tabelas de sinais e backtest dentro do cartão, cabeçalhos em duas linhas, formulário de operação e de alerta com as ações visíveis sem rolagem.
- **Portfólio:** uma atualização que falha mostra "Indisponível" nos totais em vez de manter os valores anteriores; operações de ativos fora da watchlist exibem o ticker informado pelas posições.
- **Notificações:** a consulta periódica continua com Portfólio, Alertas ou Chat abertos; clicar no aviso da bandeja restaura a janela minimizada.
- **Chat:** o corte do histórico nunca envia uma resposta sem a pergunta que a originou.
- **Mensagens:** detalhes que o backend envia sem acentuação ou com nomes de campo ganharam redação própria; erros de configuração informam o motivo; indicadores e contexto de sinais aparecem como "chave: valor" em vez de JSON.
- **Demais ajustes:** salvar a preferência de notificação não recarrega cotação e histórico; item realçado nas listas suspensas; atalhos de teclado do login sem conflito; rótulos do eixo do gráfico com casas decimais proporcionais ao intervalo.

Evidência: compilação sem avisos, 75 testes aprovados (eram 40) e capturas de todas as telas em 125% com respostas simuladas. Nesta máquina o Smart App Control bloqueou algumas compilações intermediárias; a execução final rodou sem bloqueio.

Não validado nesta revisão: escalas de 150% e 200%, troca de monitor com a janela aberta, e os itens que dependem do servidor (sinais gerados pelo agendador, aviso na bandeja com notificação real e IA com provedor configurado).

Sugestões ao backend, sem alteração feita por este repositório: mensagem legível para `QuantidadeInsuficienteError` (hoje o `detail` é o UUID do ativo), `id` no resultado da busca e moeda nas posições.

## Sincronização com o backend em 08/10/2026

O backend `develop` avançou do commit `a175cb6` para o `3ed5a8c`. O contrato novo e o tratamento de cada mudança estão em [Integração com a API](INTEGRACAO_API.md#atualização-de-08102026-backend-develop-em-3ed5a8c). O backend não foi alterado.

- **Sessão (marco 1):** cadastro, login e refresh devolvem access token de 30 minutos e refresh token. O cliente renova o access token perto do vencimento ou ao receber 401, uma renovação por vez, e repete a requisição uma única vez. As telas abertas continuam na mesma sessão. Só um refresh recusado leva ao login.
- **Logout:** sair ou fechar a janela principal revoga o login no servidor em segundo plano, além de apagar a sessão local.
- **Notificações (marco 5):** conexão WebSocket com autenticação na primeira mensagem, reautenticação antes do vencimento do token, reconexão com espera crescente e tratamento do fechamento 4401. Cada aviso dispara a consulta das não lidas, que continua deduplicando por `id`. A consulta periódica de 2 minutos permanece como complemento.
- **IA:** a análise devolvida como `desatualizada` é exibida com aviso e data.
- **Sem mudança de código:** criptomoedas via Binance, tipo `INDICE`, histórico `1D` com ponto único e séries `1A`/`5A` mais curtas.

Evidência: compilação sem avisos e 90 testes aprovados (eram 75), incluindo renovação concorrente, refresh recusado, logout e o protocolo do WebSocket contra um servidor local.

Conferido depois, no mesmo dia, contra o backend `develop` (`3ed5a8c`) executado com `docker compose up`, por chamadas HTTP diretas: cadastro e login devolvem `access_token`, `refresh_token`, `token_type` e `expires_in` (1800); o refresh devolve um par novo aceito nas rotas autenticadas; depois do logout, o refresh token revogado é recusado com 401 "Refresh token invalido".

Não validado: o WebSocket e o aviso na bandeja com notificação real, e uma sessão de mais de 30 minutos em uso contínuo.

## Acompanhamento do cliente web em 09/10/2026

O web integrou carteira, chat IA, alertas em tempo real e web push à API real (commit `42f9176`). O contrato da API não mudou desde a sincronização de 08/10/2026; o desktop passou a aproveitar as mesmas rotas como o web. A comparação completa, com o que foi mantido diferente, está em [Integração com a API](INTEGRACAO_API.md#atualização-de-09102026-cliente-web-em-42f9176). Web e backend não foram alterados.

- **IA (marco 6):** análise e chat esperam 70 s e 90 s. O prazo de 30 s, antes aplicado a todas as chamadas, interrompia respostas mais lentas do provedor. As telas avisam que a resposta pode levar até 1 minuto.
- **Visão geral (marco 2):** a watchlist mostra preço, variação do dia e quantidade de sinais vigentes de cada ativo.
- **Alertas (marco 4):** preço atual, distância até o alvo e contagem de alertas com a condição satisfeita no preço atual.
- **Portfólio (marco 6):** variação do dia e resultado por posição, variação do dia da carteira, CDI e Ibovespa carregados com a tela, aba com o resumo diário e sugestão da cotação atual no registro de operação.
- **Notificações (marco 5):** ação para marcar todas como lidas.
- **Layout:** Alertas e Notificações deixaram de cortar o painel da direita em janelas mais estreitas que o tamanho inicial; os tamanhos mínimos de Alertas e Portfólio subiram para caber as colunas novas.

Evidência: compilação sem avisos, 93 testes aprovados (eram 90) e capturas de todas as telas em 125%, no tamanho padrão e no mínimo, com dados do backend local em Docker (`a8bcb02`). Com esse backend foram conferidos uma análise por IA (18,7 s, `gemini-3.5-flash`) e um resumo diário gerado pelo job do servidor.

Não validado: o chat com o provedor real, o aviso na bandeja com notificação real, escalas de 150% e 200%, e o comportamento com muitas dezenas de ativos na watchlist (duas chamadas por ativo a cada atualização da lista).

### Revisão e limpeza no mesmo dia

Depois da entrega acima, o código do desktop foi revisto por inteiro com os analisadores do .NET ligados; nenhum membro sem uso foi apontado. O que mudou:

- **Nova operação:** ao trocar de ativo, a cotação dele entra como preço unitário, e a tela mostra o valor total (quantidade × preço) enquanto se digita.
- **Aviso legal:** análise, chat e resumo diário mostram "Análise gerada por IA. Não constitui recomendação de investimento." mesmo quando a API não envia o texto; o chat já abre com o aviso.
- **Código:** quatro auxiliares iguais, um em cada tela, viraram um só (`OptionalRequest.OrNullAsync`); saíram os aliases de cor sem uso do tema; comentários em inglês foram traduzidos.
- **README:** reorganizado em torno do estado atual; a base documental e o histórico das sincronizações ficam em `docs/`.

Evidência: compilação sem avisos e 94 testes aprovados (eram 93). A linguagem da interface foi conferida por busca nos textos: não há chamadas para comprar ou vender.

### Pendências do card do desktop (issue #10 do backend)

O card pede gráficos de preço e indicadores fáceis de ler, e a regra do aviso legal nas telas com texto de IA. O aviso entrou na revisão acima; aqui entram o gráfico e as validações que faltavam com dados reais.

- **Gráfico (marco 3):** linha de fechamento ou candles, SMA 20, SMA 50, bandas de Bollinger e, no painel inferior, volume ou RSI (14) com as faixas de 30 e 70. Zoom pela roda do mouse ou pelas teclas + e −, deslocamento com Shift + roda ou pelas setas, e volta à série inteira com duplo clique ou Esc. O detalhe do registro traz os indicadores ligados. O desenho continua nativo, sem biblioteca externa; o card cita LiveCharts2 ou ScottPlot como exemplos.
- **Séries dos indicadores:** calculadas no desktop sobre o histórico recebido, com as fórmulas do backend; ver [Integração com a API](INTEGRACAO_API.md#indicadores-no-gráfico-09102026). Os valores oficiais continuam na aba Indicadores.
- **Chat IA validado:** uma pergunta enviada pela tela ao backend local com o provedor real respondeu em 4,9 s (`gemini-3.5-flash`), com o aviso legal.
- **Aviso na bandeja validado:** com a janela principal aberta e o WebSocket conectado, um alerta de preço disparado pelo ciclo do agendador do backend chegou em poucos segundos, atualizou o contador ("Notificações (4)") e o Windows exibiu o balão.

Evidência: compilação sem avisos, 97 testes aprovados (eram 94) e capturas do gráfico em 125% com dados reais (linha, candles com Bollinger e RSI, e zoom).

Continuam fora do alcance do desktop sozinho: notificações com o aplicativo fechado (não há canal de push), assinatura do executável (exige certificado) e a validação em 150% e 200% (a máquina de desenvolvimento usa 125%).

## Pendências do status da issue #10 (12/10/2026)

O status enviado à equipe listava três pendências do desktop: notificações com o aplicativo fechado, instalador e assinatura, e validação em 150% e 200% de escala. O backend e o web não foram alterados; o contrato da API é o mesmo de 08/10/2026 (backend `main` em `a8bcb02`).

- **Segundo plano (marco 5):** fechar a janela principal deixa o aplicativo na bandeja, com a sessão, o WebSocket e a consulta periódica ativos. O ícone ganhou menu (Abrir, Notificações, Encerrar) e duplo clique que abre a janela. O aplicativo passou a ter uma instância por usuário do Windows: abrir de novo traz a janela que já está rodando. "Encerrar o InsightFlow" e o logoff do Windows fecham de verdade e revogam o login, como antes fazia o fechamento da janela.
- **Configurações:** o item da barra lateral deixou de ser um espaço reservado. A tela guarda duas preferências por usuário do Windows: continuar em segundo plano ao fechar a janela (ligada por padrão) e abrir o InsightFlow ao iniciar o Windows (desligada por padrão). As preferências ficam em `%LOCALAPPDATA%\InsightFlow\preferences.json`; não há tokens nem senhas gravados.
- **Instalador (marco 7):** `installer/build.ps1` publica o executável único e compila `installer/InsightFlow.iss` com o Inno Setup. A instalação é por usuário, sem administrador, preserva o `appsettings.json` numa atualização e a desinstalação remove preferências e a entrada de inicialização. O projeto passou a declarar a versão (`1.0.0`), que nomeia o instalador.
- **Assinatura (marco 7):** `installer/sign.ps1` assina executável, desinstalador e instalador com um `.pfx` indicado por variável de ambiente; o workflow faz o mesmo quando os segredos `SIGN_PFX_BASE64` e `SIGN_PFX_PASSWORD` existem. O projeto ainda não tem certificado, então os binários continuam saindo sem assinatura.
- **Escala do Windows:** a validação encontrou defeitos, corrigidos nesta entrega:
  - na área útil de 150% num monitor 1080p (1280 × 672 pixels lógicos), a Visão geral escondia as linhas da watchlist e trocava o gráfico por "Amplie a janela". A tela ganhou um modo compacto para janelas baixas e o tamanho mínimo passou de 1120 × 740 para 1200 × 664, que cabe nessa área;
  - o gráfico mostra só o painel de preço quando não há altura para os dois painéis, em vez do aviso;
  - quando nem o tamanho mínimo projetado cabe na área útil (175% nesse monitor), todas as telas mantêm o layout e a janela rola, em vez de espremer e sobrepor os controles;
  - Portfólio e Chat cortavam a borda direita no tamanho mínimo; Alertas e Notificações cortavam campos e botões do painel da direita em 175%; a barra de busca da Visão geral perdia a borda inferior acima de 100%.

Evidência:

- compilação sem avisos e 110 testes aprovados (eram 97), incluindo preferências, inicialização com o Windows, instância única, fechamento para a bandeja, modo compacto e rolagem;
- no aplicativo real contra o backend local: a segunda abertura encerra sozinha, fechar a janela mantém o processo sem janela visível, abrir de novo traz a janela de volta, "Sair" volta ao login e fechar o login encerra o aplicativo;
- com a janela fechada para a bandeja, um alerta de preço disparado pelo ciclo do agendador do backend chegou em 5,7 s pelo WebSocket, atualizou o contador ("Notificações (5)") e o Windows exibiu o balão, com a janela ainda oculta;
- capturas das sete telas em 125%, 150% e 175% reais, no tamanho padrão e no mínimo, num monitor 1920 × 1080, com a escala trocada sem novo logon e o processo iniciado depois da troca (DPI do sistema igual ao da janela): nenhum controle cortado. Em 150% nenhuma tela rola; em 175% rolam Entrar, Portfólio e Visão geral;
- instalador de 44,7 MB compilado nesta máquina com o Inno Setup 6.7.3, sem assinatura e também com um certificado de teste autoassinado (executável e instalador assinados; certificado não confiável, como esperado).

Não validado:

- 200% real: o Windows oferece no máximo 175% em monitores 1920 × 1080. A área útil equivalente (960 × 492) foi simulada em 125%, com rolagem em todas as telas, menos o Chat, que cabe;
- mover a janela entre monitores de escalas diferentes;
- a execução do instalador nesta máquina: o Smart App Control bloqueia executáveis sem assinatura confiável. O job `installer` do workflow instala e desinstala em modo silencioso, e roda a partir do primeiro envio desta entrega ao GitHub;
- o carimbo de tempo da assinatura (o teste local assinou sem ele) e a assinatura com um certificado confiável.

Continuam fora do alcance do desktop sozinho: receber com o processo encerrado ou antes da entrada (exige um canal de push do Windows no backend, ou guardar a sessão no computador, o que muda a decisão de manter tokens só em memória) e o certificado de assinatura de código (tem custo e exige validação de identidade).
