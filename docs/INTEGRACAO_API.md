# Integração com a API InsightFlow

## Segundo plano na bandeja (12/10/2026)

Mudança só no desktop: o contrato da API é o mesmo da seção de 08/10/2026 (backend `main` em `a8bcb02`) e o backend não foi alterado.

- **Conexão:** fechar a janela principal a oculta em vez de encerrá-la. A conexão `WS /notificacoes/ws`, a renovação do token e a consulta de `/notificacoes?apenas_nao_lidas=true` a cada 2 minutos continuam, e cada notificação nova vira aviso na bandeja. Onde as seções abaixo dizem "enquanto a janela está aberta", leia "enquanto o aplicativo está em execução".
- **Sessão:** continua só em memória. Com o aplicativo dias na bandeja, o access token é renovado a cada 30 minutos e o refresh token (30 dias) gira a cada renovação. Um refresh recusado encerra a sessão e abre a tela de entrada com o aviso de sessão expirada.
- **Logout:** `POST /auth/logout` é enviado em "Sair" e em "Encerrar o InsightFlow"; fechar a janela para a bandeja não revoga nada.
- **Limite:** com o processo encerrado ou antes da entrada não há conexão. O `/dispositivos` (push Expo) e o `/web-push/*` (VAPID) são os canais do mobile e do web; um canal equivalente para o desktop (notificações do Windows) exigiria novo contrato no backend.

Conferido com o backend local: com a janela oculta na bandeja, um alerta de preço disparado pelo ciclo do agendador chegou pelo WebSocket em 5,7 s e gerou o aviso do Windows.

## Indicadores no gráfico (09/10/2026)

`GET /ativos/{id}/indicadores` devolve só o valor mais recente de cada indicador. Para desenhar a evolução no gráfico, o desktop calcula as séries de SMA 20, SMA 50, Bollinger (20, 2) e RSI (14) sobre os fechamentos do histórico, como o cliente web faz. São médias e desvios de uma lista de preços; sinais, backtest, carteira e textos de IA continuam vindo prontos da API.

- **Fórmulas:** as mesmas do backend (pandas-ta): Bollinger com desvio-padrão amostral e RSI com suavização de Wilder iniciada na primeira variação.
- **Registros anteriores:** além do período escolhido, a tela pede o histórico `5A` do ativo (os candles diários que o backend guarda) para as médias não começarem vazias. Se essa consulta falhar, os indicadores usam só o período exibido. `1D` (um ponto) e `5A` não fazem a segunda consulta.
- **Conferência:** com o backend local em `a8bcb02`, o último valor de cada série calculada no desktop foi igual ao da API em PETR4, VALE3 (71 registros) e BTC (1000 registros), para SMA 20, SMA 50, RSI 14 e as duas bandas de Bollinger.

Os valores exibidos na aba Indicadores continuam sendo os da API.

## Atualização de 09/10/2026: cliente web em `42f9176`

O commit `42f9176` do `Insight-flow-web` (PR #15, "integra carteira, chat IA, alertas em tempo real e web push com a API real") foi lido para conferir como o web passou a usar a API. O contrato é o mesmo da seção de 08/10/2026: o backend só recebeu um commit desde então (`d3f80c6`, que carrega o `.env` no container) e o `openapi.json` da instância local lista as mesmas rotas. Web e backend não foram alterados. O desktop passou a usar as rotas que já consumia da mesma forma que o web:

| O que o web faz com a API | No desktop |
| --- | --- |
| Espera até 70 s pela análise (`GET /ativos/{id}/analise`) e 90 s pelo chat (`POST /chat`); as demais chamadas têm 20 s. | O prazo passou a ser contado por chamada. `Api.TimeoutSeconds` (30 s por padrão) vale para as chamadas comuns; análise e chat esperam 70 s e 90 s, ou o valor configurado, se for maior. Antes, o prazo único de 30 s interrompia respostas de IA mais lentas. |
| Watchlist com a cotação e a quantidade de sinais vigentes de cada ativo (`/cotacao` e `/sinais` por item). | A grade da watchlist ganhou Preço, Var. dia e Sinais. Cada ativo falha em separado: sem cotação, a célula fica vazia. |
| Alertas com o preço atual e o progresso até o alvo (uma cotação por ativo, não por alerta). | Colunas Preço atual e Distância (quanto o preço precisa variar para chegar ao alvo) e contagem dos alertas com a condição satisfeita no preço atual. O estado continua sendo o `ultimo_estado` do backend. Preço e alvo só são comparados quando estão na mesma moeda. |
| Posições com a variação do dia, vinda da cotação, e o resultado percentual; carteira comparada com CDI e Ibovespa ao abrir. | Colunas Var. dia e Resultado, variação do dia da carteira ponderada pelo valor em BRL, e os dois benchmarks consultados junto com a tela (o botão "Comparar" saiu). |
| Resumo diário lido da notificação `RESUMO_DIARIO` mais recente (`contexto.texto` e `contexto.aviso_legal`). | Aba "Resumo diário" no Portfólio, com o aviso legal. |
| Nova operação sugere a cotação atual como preço e mostra a quantidade em carteira. | O formulário mostra a cotação atual e a quantidade em carteira do ativo escolhido. Ao trocar de ativo, a cotação dele entra como preço unitário, que continua editável. A venda acima da posição continua sendo recusada pela API, que avalia a data informada. |
| Marca todas as notificações como lidas de uma vez. | Botão "Marcar todas como lidas" em Notificações: um `PATCH` por notificação; as que falharem continuam não lidas. |

Diferenças mantidas de propósito:

- **Períodos do histórico:** o web oferece só `1M` e `3M`. O backend local respondeu a todos (PETR4: 1, 5, 22, 64, 71 e 71 pontos; BTC: 1, 7, 30, 90, 365 e 1000), então o desktop mantém os seis.
- **Ticker do alerta:** o web limita a lista à watchlist. O backend aceitou alertas para tickers fora dela (ITUB4 e VALE3), então o campo continua livre.
- **Operação de ativo fora da watchlist:** o web inclui o ativo na watchlist sem perguntar; o desktop continua exigindo a inclusão explícita.
- **Web push e service worker:** canal do navegador. O desktop recebe pelo WebSocket enquanto está aberto.
- **Notícias, rankings, score, sessões, perfil e recuperação de senha:** continuam simulados no web, sem rota no backend.

Verificação: testes automatizados e as telas abertas contra o backend local em Docker (`a8bcb02`), com uma conta de teste: watchlist com três ativos, três alertas, quatro operações, uma análise por IA (18,7 s) e um resumo diário gerado pelo job do backend. Não validado nesta rodada: o chat com o provedor real e o aviso na bandeja.

## Atualização de 08/10/2026: backend `develop` em `3ed5a8c`

O código da branch `develop` do backend foi relido do commit `a175cb6` até o `3ed5a8c` (PRs #39 a #50), junto com o `docs/api-reference.html` que agora acompanha o repositório. O backend não foi alterado. Esta seção prevalece sobre as seguintes onde houver diferença.

O que mudou no contrato e como o desktop passou a tratar:

| Mudança no backend | Efeito no desktop |
| --- | --- |
| `TokenResponse` ganhou `refresh_token` e `expires_in` (segundos). O access token passou de 24 horas para **30 minutos**; o refresh token vale 30 dias. | Os dois campos são obrigatórios na resposta de cadastro, login e refresh. A sessão guarda, em memória, os dois tokens e o vencimento. |
| `POST /auth/refresh` `{refresh_token}` → novo par; o refresh token anterior é invalidado (rotação). Reuso de um refresh token revoga o login inteiro. | O cliente HTTP renova o access token quando faltam menos de 60 s para o vencimento e, diante de um 401 em rota autenticada, renova e repete a requisição **uma vez**. As renovações são serializadas: requisições paralelas esperam a mesma renovação. Refresh recusado (401) encerra a sessão e leva ao login. |
| `POST /auth/logout` `{refresh_token}` → 204, revoga aquele login. | "Sair" e o fechamento da janela principal apagam a sessão local e enviam a revogação em segundo plano; falha de rede não impede a saída. Sessão expirada não envia logout. |
| `WS /notificacoes/ws`: a primeira mensagem é `{"tipo":"autenticar","token":"<access_token>"}` (em até 10 s); o servidor responde `{"tipo":"autenticado"}` e passa a enviar `{"tipo":"notificacao","notificacao":{…}}` e `{"tipo":"ping"}` a cada 30 s. Fecha com **4401** se o token for inválido ou expirar. | A janela principal mantém a conexão enquanto está aberta. A cada conexão autenticada e a cada notificação, consulta `/notificacoes?apenas_nao_lidas=true` e avisa pela bandeja. Antes do vencimento, renova o token e envia `autenticar` de novo na mesma conexão; num 4401, renova e reconecta. Silêncio de 90 s ou queda reconecta com espera crescente (2 s a 60 s). O token nunca vai na URL nem em cabeçalho. |
| O WebSocket não reenvia o que chegou com a conexão caída. | A consulta periódica de 2 minutos foi mantida como complemento e como única via em servidores sem o WebSocket. |
| `AnaliseIAResponse` ganhou `desatualizada`: verdadeiro quando o provedor de IA falhou e o backend devolveu a última análise guardada (sempre com `em_cache: true`). | Campo obrigatório; a aba "Análise IA" avisa que a análise é a última guardada, com a data, e pode estar desatualizada. O 503 só ocorre quando não há análise anterior. |
| Criptomoedas via Binance: o ticker é o código da moeda (`BTC`, `ETH`…), `tipo: "CRIPTO"`, sempre em `BRL`; variação e máxima/mínima são das últimas 24 h. | Sem mudança de código: busca, watchlist, alertas e operações já usam ticker, tipo e moeda como vêm da API. |
| `TipoAtivo` ganhou `INDICE` (Ibovespa, uso interno do benchmark; não aparece em busca nem watchlist). | Sem mudança: o tipo é exibido como texto. |
| Histórico: `1D` devolve **um único ponto** montado com a cotação atual; `1A`/`5A` devolvem os candles diários que o backend já guardou e podem vir mais curtos que o período; cache de 24 h por período. | Sem mudança: o gráfico e a tabela já aceitam um ponto e séries de qualquer tamanho. |
| Benchmark Ibovespa funciona no plano gratuito da brapi; continua `null` quando a fonte está indisponível. | Sem mudança. |
| Toda resposta HTTP traz `X-Request-ID`; CORS configurável; logs estruturados; limite de taxa e retentativa nas integrações. | Sem efeito no desktop (CORS não se aplica a cliente nativo). O `X-Request-ID` ainda não é exibido nas mensagens de erro. |
| `POST`/`DELETE /dispositivos` (push Expo) e `/web-push/*` (VAPID). | Não se aplicam: são os canais do mobile e do web. O desktop recebe pelo WebSocket enquanto está aberto. |

Erros novos: 401 `"Refresh token invalido"` no refresh (expirado, revogado, já usado ou usuário desativado) e 503 nas rotas de mercado quando a fonte (brapi ou Binance) está indisponível, com a mesma mensagem de antes.

Pendências resolvidas por esta versão do backend: refresh de sessão, logout remoto e entrega de notificações em tempo real. Continuam: busca sem `id`, recuperação de senha, moeda nas posições, histórico de dividendos e notificações com o aplicativo fechado (o desktop não tem canal de push).

Verificação: testes automatizados com respostas HTTP controladas e um servidor WebSocket local. Cadastro, login, refresh e logout também foram conferidos por chamadas HTTP contra esta versão do backend em Docker; o WebSocket ainda não foi exercitado com o servidor real.

## Atualização de 30/09/2026: código do backend conferido

O código da branch `develop` do repositório `InsightF-AI/Insight-flow-backend` (commit `a175cb6`, 29/09/2026) foi lido para confirmar o contrato. O backend não foi alterado nem executado nesta etapa. A branch `feature/alerta-service`, base da referência HTML abaixo, não existe mais no repositório.

As 16 rotas originais continuam com os mesmos caminhos e campos, com uma correção: **`ultimo_estado` do alerta é booleano** (verdadeiro quando a condição estava satisfeita na última avaliação), não texto. O desktop esperava texto; com o backend atual, toda a lista de alertas seria rejeitada como formato incompatível.

Rotas novas, todas autenticadas, já consumidas pelo desktop:

| Método | Caminho | Entrada e resultado principal |
| --- | --- | --- |
| GET | `/notificacoes?apenas_nao_lidas=` | Lista `NotificacaoResponse` (`id`, `ativo_id?`, `tipo`, `mensagem`, `contexto`, `lida`, `criado_em`), da mais recente para a mais antiga. |
| PATCH | `/notificacoes/{notificacao_id}/lida` | Sem corpo → 200, notificação com `lida: true`. |
| POST | `/operacoes` | `{ativo_id, tipo: COMPRA\|VENDA, quantidade > 0, preco_unitario > 0, data: AAAA-MM-DD}` → 201. Data futura, venda acima da posição → 422. |
| GET | `/operacoes` | Lista de operações do usuário. |
| DELETE | `/operacoes/{operacao_id}` | 204; 422 se a remoção deixar vendas sem saldo. |
| GET | `/portfolio/posicoes` | Posições abertas; ativos sem cotação são omitidos. Valores na moeda do ativo, exceto `valor_mercado_brl`. |
| GET | `/portfolio/rentabilidade` | Custos, valores e lucros em BRL; `percentual` é **fração** (0,12 = 12%). |
| GET | `/portfolio/benchmark?benchmark=CDI\|IBOVESPA` | Rentabilidade da carteira e do índice, **frações**; índice `null` se a fonte estiver indisponível; 404 sem operações. |
| GET | `/portfolio/distribuicao` | `por_classe`, `por_setor` (`N/A` sem setor), `por_moeda`: **frações** do valor de mercado em BRL. |
| GET | `/ativos/{ativo_id}/analise` | Texto gerado por IA, `gerado_em`, `modelo`, `em_cache`, `aviso_legal`. 422 com dados insuficientes. |
| POST | `/chat` | `{mensagens: [{papel: usuario\|assistente, texto}]}`, última do usuário, até 20 mensagens de até 2000 caracteres (padrões configuráveis no servidor) → `{texto, modelo, aviso_legal}`. |

Semânticas confirmadas no código:

- Decimais chegam como **strings JSON** (serialização padrão do Pydantic); o cliente aceita número ou string.
- Retornos do backtest são **pontos percentuais**: `(preço futuro / preço base − 1) × 100`.
- Notificações são geradas pelo agendador do backend (sinais a cada 15 min para renda variável e 5 min para cripto; resumo diário às 18h30). Não há push, SSE ou WebSocket: o desktop consulta `/notificacoes?apenas_nao_lidas=true` a cada 2 minutos enquanto a janela principal está aberta e avisa pela bandeja do Windows.
- Erros de IA: **503** para provedor desabilitado/indisponível ou cota excedida (esta com `Retry-After`); **502** quando a resposta é bloqueada pelo guardrail. A IA fica desabilitada por padrão (`AI_HABILITADA=false`).
- As operações exigem o UUID interno do ativo, que só existe após o ativo ser cadastrado no backend (por exemplo, ao entrar na watchlist). O desktop oferece para operações os ativos da watchlist.

Pendências que continuam: busca sem `id`, refresh de sessão, recuperação de senha, moeda nas posições, histórico de dividendos e contrato de entrega de notificações em tempo real.

## Origem e grau de confirmação (referência HTML de 15/09/2026)

Este guia resume o arquivo fornecido `api-reference.html`, atualizado em **15/09/2026**, referente à branch **`feature/alerta-service`** do backend. O próprio rodapé declara que a referência ainda não constitui um contrato versionado. As referências abaixo usam as seções do HTML original.

**Documentado:** rotas, exemplos e comportamentos descritos nessa referência. **Verificado nesta etapa:** leitura do documento, sem executar a API, os exemplos ou os comandos de ambiente. Não há confirmação de que o backend publicado já implementa esse contrato. Conferir os schemas/OpenAPI e o commit do backend antes da integração definitiva.

## Transporte e sessão

- Base de desenvolvimento: `http://localhost:8000`; prefixo dos recursos: `/api/v1`; conteúdo JSON. A URL deve ser configurável por ambiente, sem credenciais embutidas.
- `GET /health` fica fora do prefixo; o corpo de resposta não foi especificado.
- Rotas privadas exigem `Authorization: Bearer <access_token>`. Cadastro e login retornam `{access_token, token_type: "bearer"}`.
- O cadastro já autentica. Expiração padrão documentada: **1440 minutos / 24 horas**. Não existem endpoints documentados de refresh ou logout.
- No desktop, sair deve apagar a sessão local; isso não revoga o token no servidor. Quando a sessão deixar de ser válida, solicitar novo login, sem tentar refresh inexistente.
- A persistência local do token é uma decisão do desktop: usar armazenamento protegido do sistema operacional caso a sessão seja persistida. Não guardar senha ou token em arquivos de configuração, logs ou repositório.
- Watchlist e alertas pertencem ao usuário do token; não enviar um identificador de usuário inventado.
- A referência informa ausência de `CORSMiddleware`. Confirmar o transporte do desktop: chamadas feitas por uma WebView podem exigir configuração de origem no backend.
- Cotação e histórico usam brapi.dev. Segundo a referência, sem `BRAPI_API_KEY` **no backend**, funcionam apenas `PETR4`, `MGLU3`, `VALE3` e `ITUB4`; busca funciona sem chave. O desktop deve consumir o backend, sem receber a chave do provedor.

Fontes: §1 Visão geral, §3 Autenticação, §4 Ativos, §5 Watchlist e §6 Alertas.

## Rotas documentadas

Os caminhos da tabela usam o prefixo `/api/v1`, exceto `/health`. São **16 rotas de recurso e 1 health check**. Todas as rotas de ativos, watchlist e alertas exigem autenticação; cadastro e login são públicos. A autenticação de `/health` não foi detalhada.

| Método | Caminho | Entrada e resultado principal |
| --- | --- | --- |
| GET | `/health` | Saúde do backend; corpo e status não especificados. |
| POST | `/usuarios` | `{nome, email, senha}` → 201, `TokenResponse`. |
| POST | `/auth/login` | `{email, senha}` → 200, `TokenResponse`. |
| GET | `/ativos/buscar?termo=` | Ticker ou nome → 200, lista de resultados; pode ser vazia. |
| GET | `/ativos/{ativo_id}/cotacao` | UUID interno → 200, cotação. |
| GET | `/ativos/{ativo_id}/historico?periodo=` | UUID e período, padrão `1M` → 200, lista OHLCV. |
| GET | `/ativos/{ativo_id}/indicadores` | UUID → 200, lista dos indicadores suportados. |
| GET | `/ativos/{ativo_id}/sinais` | UUID → 200, sinais vigentes ou recém-desativados. |
| GET | `/ativos/{ativo_id}/sinais/{regra_id}/backtest` | Dois UUIDs → 200, ocorrências e retornos médios. |
| POST | `/watchlist` | `{ticker}` → 201, `ItemWatchlistResponse`; cria ativo na base se necessário. |
| GET | `/watchlist` | Lista dos itens do usuário, com o mesmo formato do POST. |
| DELETE | `/watchlist/{ativo_id}` | UUID do ativo → 204, sem corpo. |
| PATCH | `/watchlist/{ativo_id}/notificacao` | `{notificar: boolean}` → 200, `ItemWatchlistResponse`. |
| POST | `/alertas` | `{ticker, tipo_condicao, valor_alvo}` → 201, `AlertaResponse`. |
| GET | `/alertas` | Lista dos alertas habilitados e desabilitados do usuário. |
| PATCH | `/alertas/{alerta_id}` | Campos opcionais `tipo_condicao`, `valor_alvo`, `ativo` → 200, `AlertaResponse`. |
| DELETE | `/alertas/{alerta_id}` | UUID do alerta → 204, sem corpo. |

Fontes: §1 Visão geral e §3–§6. Os exemplos não especificam status e envelope de todas as listagens; conferir os schemas reais.

## Modelos e nomes críticos

Os campos abaixo resumem exemplos documentados; não substituem schemas com tipos, obrigatoriedade e nulabilidade completos.

| Modelo/resposta | Campos documentados |
| --- | --- |
| `TokenResponse` | `access_token`, `token_type` |
| Resultado da busca | `ticker`, `nome`, `tipo`, `moeda`, `setor`; **não inclui `id` no exemplo** |
| `AtivoResumoResponse` | `id`, `ticker`, `nome`, `tipo`, `setor`, `moeda` |
| Cotação | `ticker`, `preco`, `variacao`, `variacao_percentual`, `maxima_dia`, `minima_dia`, `volume` |
| Item OHLCV | `data`, `abertura`, `maxima`, `minima`, `fechamento`, `volume` |
| Indicador | `tipo`, `parametros`, `data_calculo`, `valor`, `valores_auxiliares` |
| Sinal | `id`, `ativo_id`, `regra_id`, `regra_nome`, `regra_descricao`, `data_ativacao`, `contexto`, `data_desativacao` |
| Backtest | `regra_id`, `ativo_id`, `total_ocorrencias`, `retorno_medio_5_pregoes`, `retorno_medio_20_pregoes`, `retorno_medio_60_pregoes` |
| `ItemWatchlistResponse` | `ativo: AtivoResumoResponse`, `notificar`, `adicionado_em` |
| `AlertaResponse` | `id`, `ativo: AtivoResumoResponse`, `tipo_condicao`, `valor_alvo`, `moeda_alvo`, `habilitado`, `ultimo_estado`, `disparado_em`, `criado_em`, `atualizado_em` |

Regras de implementação:

- **Ticker e UUID são distintos.** Usar UUID interno em `ativo_id`, `alerta_id` e `regra_id`. Nunca interpolar ticker no lugar de UUID.
- **PATCH de alerta:** o request usa `ativo: boolean` para habilitar/desabilitar; a resposta usa `habilitado: boolean`. Na resposta, `ativo` é o objeto do instrumento. Separar DTOs de escrita e leitura.
- Cadastro: `nome` de 1–255 caracteres, e-mail válido e `senha` de 8–128 caracteres. Criação de alerta: `ticker` de 1–20 caracteres e `valor_alvo` decimal maior que zero.
- PATCH é parcial; enviar somente os campos alterados. A referência informa que payload vazio de atualização de alerta pode gerar 422.
- `valores_auxiliares` pode ser `null`; o objeto é usado em indicadores como MACD/Bollinger, mas suas chaves não foram especificadas.
- `data_desativacao: null` representa sinal vigente; o retorno também pode conter sinais desativados. `disparado_em` do alerta pode ser `null`.
- Retornos de backtest podem ser `null` quando falta histórico para fechar a janela. Exibir indisponibilidade, sem convertê-los em zero. A unidade desses retornos precisa ser confirmada antes de formatar como percentual.
- Datas aparecem como strings ISO 8601 com `Z` nos exemplos. Preservar a informação de fuso e confirmar semântica das datas de pregão.
- Os exemplos de dinheiro usam números JSON; precisão, arredondamento e eventual serialização decimal como string precisam ser confirmados.
- Respostas 204 não contêm JSON. Não tentar desserializar corpo vazio.
- A cotação não inclui timestamp no exemplo. Não apresentar horário de atualização de mercado como se fosse fornecido pela API.

Fontes: §3 Autenticação, §4 Ativos, §5 Watchlist e §6 Alertas.

## Erros e estados de interface

O cliente deve normalizar os dois formatos de erro documentados em §2 Erros & formatos:

```json
{"detail": "Ativo nao encontrado"}
```

```json
{"detail": [{"loc": ["body", "valor_alvo"], "msg": "Input should be greater than 0", "type": "greater_than"}]}
```

| Status | Significado documentado e tratamento esperado |
| --- | --- |
| 401 | Credenciais de login incorretas, token inválido/expirado ou usuário removido; distinguir falha de login de sessão encerrada. |
| 403 | Header `Authorization` ausente, segundo o `HTTPBearer` descrito; conferir antes de interpretar como outra proibição. |
| 404 | Ativo, cotação, histórico, alerta, item de watchlist ou regra ausente, conforme a rota. |
| 409 | E-mail já cadastrado ou ativo já presente na watchlist. |
| 422 | Validação Pydantic ou regra de negócio; `detail` pode ser lista ou texto. |
| 503 | Fonte de dados de mercado indisponível. |

A tabela original menciona token ausente tanto em 401 como em 403; validar essa diferença no backend. Não depender de correspondência exata com mensagens em português para controlar o fluxo. Exibir estados de carregamento, vazio, indisponibilidade e falha de rede separadamente; prever erro inesperado sem expor detalhes internos.

## Enums e regras de sinal

Preservar os valores literais do contrato; adaptar apenas os rótulos de apresentação. Fonte: §7 Enums.

| Enum | Valores |
| --- | --- |
| `TipoAtivo` | `ACAO`, `FII`, `ETF`, `BDR`, `CRIPTO` |
| `TipoCondicaoAlerta` | `PRECO_MAIOR_IGUAL`, `PRECO_MENOR_IGUAL` |
| `TipoIndicador` | `SMA`, `RSI`, `MACD`, `BOLLINGER`, `VOLUME_RELATIVO` |
| `PeriodoHistorico` | `1D`, `1S`, `1M`, `3M`, `1A`, `5A` |

O §8 Regras de sinal padrão descreve três regras fixas, sem configuração pelo usuário:

| Regra | Condição | Peso | `regra_id` |
| --- | --- | --- | --- |
| Sobrevenda RSI | RSI(14) abaixo de 30 | 1.0 | `a1a1a1a1-0000-0000-0000-000000000001` |
| Sobrecompra RSI | RSI(14) acima de 70 | 1.0 | `a1a1a1a1-0000-0000-0000-000000000002` |
| Pico de Volume | Volume relativo(20) acima de 1.5× a média | 0.5 | `a1a1a1a1-0000-0000-0000-000000000003` |

Preferir o `regra_id` retornado pelo sinal ao solicitar seu backtest. Não presumir endpoint de criação ou edição de regras.

## Funcionalidades sem contrato nesta referência

- **Entrega de notificações:** o HTML documenta configuração `notificar` e estados de alerta, mas não documenta caixa de notificações, WebSocket, SSE, push, periodicidade de avaliação ou regras de redisparo. Configurar um alerta não comprova entrega de notificação nativa no desktop.
- **IA e portfólio:** não existem rotas documentadas para chat/explicação por IA, resumo diário, carteira, posições, operações ou rentabilidade de portfólio. Watchlist representa acompanhamento de ativos e não oferece esse contrato de carteira.
- Não há rotas documentadas de recuperação/alteração de senha, refresh, logout remoto ou edição de regras de sinal.

Essas ausências dizem respeito ao HTML lido; não demonstram que tais recursos nunca serão implementados. Requisitos de outras fontes devem permanecer identificados como planejados até haver contrato confirmado.

## Pendências para confirmar com o backend

1. **Busca → detalhe:** a busca não apresenta UUID, mas detalhes exigem `ativo_id`. Confirmar inclusão de `id` ou uma rota de resolução por ticker. Não adicionar watchlist/alerta silenciosamente para obter o UUID.
2. **Versão executável:** obter repositório/commit compatível, OpenAPI e URLs de homologação/produção; confirmar divergências com este documento de branch.
3. **Schemas completos:** tipos, campos opcionais, nulabilidade, formato de decimais, precisão monetária, formato das listagens e chaves de `valores_auxiliares`/`contexto`.
4. **Sessão e origem:** status reais de token ausente/inválido, política de persistência da sessão no desktop e CORS caso chamadas partam de WebView.
5. **Dados de mercado:** granularidade e ordenação histórica, fuso/pregão, ajustes de preços, timestamp da cotação, frequência de atualização, cache, limites do provedor e rate limits da API.
6. **Sinais e backtest:** janela de “recém-desativados”, comportamento sem histórico, unidade dos retornos e erros de provedor nas rotas de indicadores/sinais/backtest.
7. **Alertas e notificações:** processo que avalia condições, semântica de `ultimo_estado` e `disparado_em`, redisparo, mecanismo de entrega e contrato necessário ao desktop.
8. **Listagens e concorrência:** paginação, ordenação, limites de watchlist/alertas, duplicidade de alertas, consistência das atualizações e estratégia de repetição após falha de rede.
9. **Recursos previstos:** confirmar prioridade e contratos próprios para IA e portfólio antes de desenvolver integração real; eles continuam no escopo documental do projeto.

O §9 Rodando local descreve Docker Compose, Postgres, Redis e migrations automáticas no repositório do backend. São informações de ambiente fornecidas, sem execução ou validação nesta etapa.
