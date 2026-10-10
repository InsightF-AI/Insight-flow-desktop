# InsightFlow Desktop

Cliente Windows do sistema acadêmico de análise de ativos da B3 e criptomoedas, em C# com Windows Forms (.NET 10). Os dados, indicadores, sinais, carteira e textos de IA vêm da API centralizada em Python/FastAPI; o backend é um serviço separado e precisa estar disponível para autenticação e consultas.

## Executar no Windows

Pré-requisito: SDK .NET 10 estável. Abra `InsightFlow.Desktop.sln` em um ambiente com suporte a .NET 10 e Windows Forms, ou execute na raiz deste repositório:

```powershell
dotnet restore InsightFlow.Desktop.sln
dotnet build InsightFlow.Desktop.sln
dotnet run --project src/InsightFlow.Desktop
```

A URL padrão do servidor é `http://localhost:8000`. Para outro ambiente, configure a raiz do servidor, sem `/api/v1`:

```powershell
$env:INSIGHTFLOW_API_URL = 'https://api.exemplo.com'
dotnet run --project src/InsightFlow.Desktop
```

Com o Smart App Control do Windows ligado, o `.exe` gerado pelo build não é assinado e pode ser bloqueado ("Uma política de Controle de Aplicativo bloqueou este arquivo"). Nesse caso, execute a DLL pelo host `dotnet`:

```powershell
dotnet src/InsightFlow.Desktop/bin/Debug/net10.0-windows/InsightFlow.Desktop.dll
```

O mesmo bloqueio pode atingir a DLL depois de uma alteração no código, impedindo também `dotnet test`. Nesse caso, rode os testes no CI ou em outra máquina.

## Configuração e sessão

A variável de ambiente tem precedência sobre [appsettings.json](src/InsightFlow.Desktop/appsettings.json). Fora de loopback, a URL deve usar HTTPS. Se a configuração for inválida, a mensagem de inicialização informa o motivo.

`Api.TimeoutSeconds` (30 por padrão, de 1 a 300) é o prazo de cada chamada comum. A análise por IA e o chat esperam 70 s e 90 s, ou o valor configurado, se for maior: o provedor de IA do servidor pode levar perto de um minuto.

Senhas são enviadas apenas no cadastro/login. O access token (30 minutos) e o refresh token ficam em memória, são renovados automaticamente durante o uso e, ao sair, são apagados e revogados no servidor. O aplicativo não inclui credenciais de demonstração nem inicia o backend automaticamente.

## Distribuir

Para gerar uma pasta distribuível (requer o runtime .NET 10 Desktop na máquina de destino):

```powershell
dotnet publish src/InsightFlow.Desktop -c Release -r win-x64 --self-contained false -o publish
```

Para gerar um único `.exe` que não depende do runtime instalado (cerca de 50 MB), com o `appsettings.json` ao lado:

```powershell
dotnet publish src/InsightFlow.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

O ícone do executável, das janelas e da bandeja é [Assets/insightflow.ico](src/InsightFlow.Desktop/Assets/insightflow.ico), feito a partir do avatar da organização no GitHub. O executável gerado não é assinado; instalador e assinatura continuam pendentes.

## Funcionalidades

1. Cadastrar uma conta ou entrar com e-mail e senha.
2. Buscar um ativo e adicioná-lo explicitamente à watchlist, que mostra preço, variação do dia e quantidade de sinais vigentes de cada ativo.
3. Selecionar um item para consultar cotação, gráfico de preço com indicadores, tabela OHLCV, indicadores técnicos e sinais.
4. Selecionar um sinal para consultar ocorrências e retornos do backtest, preservando resultados indisponíveis.
5. Criar, listar, editar, habilitar/desabilitar e excluir alertas de preço, com o preço atual e a distância até o alvo.
6. Alterar a preferência de notificação da watchlist, remover um item ou encerrar a sessão.
7. Consultar notificações (sinais, alertas disparados e resumo diário), marcar uma ou todas como lidas e receber avisos na bandeja do Windows, em tempo real pelo WebSocket da API, enquanto o app está aberto.
8. Registrar operações simuladas, com a cotação do ativo como preço unitário sugerido e o valor total, e acompanhar posições com variação do dia, rentabilidade, comparação com CDI e Ibovespa, distribuição da carteira e o resumo diário gerado pelo servidor.
9. Gerar a análise por IA de um ativo e conversar com o Chat IA.

Toda tela que exibe texto de IA (análise, chat e resumo diário) mostra o aviso "Análise gerada por IA. Não constitui recomendação de investimento.": o texto enviado pela API quando existe, e o mesmo aviso fixo quando ela não o envia. A interface é informativa: não há chamadas para comprar ou vender, e verde e vermelho indicam apenas valores positivos e negativos.

O gráfico usa os registros OHLCV recebidos, em ordem cronológica: linha de fechamento ou candles, SMA 20, SMA 50, bandas de Bollinger e, no painel inferior, volume ou RSI (14). A roda do mouse e as teclas + e − aproximam; duplo clique ou Esc voltam à série inteira; as setas percorrem os registros. As séries dos indicadores são calculadas no desktop sobre o histórico recebido, com as mesmas fórmulas do backend, porque a API devolve só o valor mais recente de cada indicador (exibido na aba Indicadores). Ele preserva o horário e o fuso recebidos, exibe um ponto quando há um único registro e trata séries constantes, vazias e falhas de consulta. O eixo horizontal usa espaçamento uniforme entre registros, sem presumir uma frequência de pregão.

As tabelas guardam datas e valores tipados: clicar no título de uma coluna ordena pelo valor, e não pelo texto exibido. As janelas acompanham a escala do Windows (conferido em 125%; 150% e 200% ainda sem validação).

## Estado da integração

O aplicativo usa endpoints reais, com erros e indisponibilidade apresentados na interface. O contrato foi conferido no código do backend e exercitado contra instâncias locais em Docker; a última conferência, em 09/10/2026, usou o backend em `a8bcb02` e cobriu watchlist, cotações, sinais, alertas, portfólio, resumo diário, análise e chat por IA e o aviso na bandeja com uma notificação recebida pelo WebSocket. Ainda não há um servidor mantido pela equipe para validar a integração. A IA depende de estar habilitada no servidor.

Ainda pendentes:

- notificações com o aplicativo fechado (o desktop não tem canal de push);
- histórico de dividendos e recuperação de senha, que dependem de contrato no backend;
- instalador e assinatura do executável.

O histórico de cada sincronização com o backend e com o cliente web está em [Integração com a API](docs/INTEGRACAO_API.md) e no [plano](docs/PLANO_DESKTOP.md).

## Estrutura

```text
InsightFlow.Desktop.sln
src/InsightFlow.Desktop/
  Api/                 Cliente HTTP, WebSocket de notificações e contratos JSON
  Configuration/       Configuração da API
  Forms/               Login/cadastro, janela principal, alertas, notificações, portfólio e chat
  Presentation/        Tema, gráfico, formatos das tabelas, aviso de IA e mensagens de erro
  Session/             Sessão mantida em memória
tests/InsightFlow.Desktop.Tests/
docs/
```

O aplicativo usa os componentes do .NET/Windows Forms, sem bibliotecas externas em runtime. A pasta `Api/` é o cliente do desktop; o backend fica em outro repositório.

## Validar

```powershell
dotnet test InsightFlow.Desktop.sln
```

Os testes usam xUnit com respostas HTTP controladas e não acessam contas reais. Cobrem os contratos da API (autenticação, sessão e sua renovação, WebSocket de notificações, busca, watchlist, análise técnica, alertas, notificações, portfólio, IA, erros, cancelamento e respostas sem corpo), a configuração da URL, as mensagens de erro, os formatos de apresentação e as telas (ordenação das tabelas, layout e fluxos assíncronos).

O workflow em [.github/workflows/desktop.yml](.github/workflows/desktop.yml) executa build e testes no Windows em cada pull request e nos envios para `main` e `develop`.

## Documentação

- [Orientações de desenvolvimento](docs/ORIENTACOES_DESKTOP.md): escopo, documentos de origem, arquitetura, telas e padrões.
- [Integração com a API](docs/INTEGRACAO_API.md): endpoints, formatos, cuidados do cliente e histórico das sincronizações.
- [Plano de implementação e pendências](docs/PLANO_DESKTOP.md): sequência de entregas, critérios de aceite e decisões com a equipe.
- [Diretrizes de UX/UI](docs/UX_UI.md): identidade visual adaptada do cliente web e regras das telas.
