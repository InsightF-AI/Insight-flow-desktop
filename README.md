# InsightFlow Desktop

Cliente Windows do sistema acadêmico de análise de ativos da B3 e criptomoedas, em C# com Windows Forms (.NET 10). Os dados, indicadores, sinais, carteira e textos de IA vêm da API centralizada em Python/FastAPI; o backend é um serviço separado e precisa estar disponível para autenticação e consultas.

## Executar no Windows

Pré-requisito: SDK .NET 10 estável. Abra `InsightFlow.Desktop.sln` em um ambiente com suporte a .NET 10 e Windows Forms, ou execute na raiz deste repositório:

```powershell
dotnet restore InsightFlow.Desktop.sln
dotnet build InsightFlow.Desktop.sln
dotnet run --project src/InsightFlow.Desktop
```

A URL padrão do servidor é a API de produção, `https://api-insight.agilesoftware.net.br`, definida em [appsettings.json](src/InsightFlow.Desktop/appsettings.json). Para outro ambiente, configure a raiz do servidor, sem `/api/v1`. A homologação, publicada a cada envio para a `main` do backend, fica em `https://hom-api-insight.agilesoftware.net.br`:

```powershell
$env:INSIGHTFLOW_API_URL = 'https://hom-api-insight.agilesoftware.net.br'   # ou http://localhost:8000 com o backend local
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

Senhas são enviadas apenas no cadastro/login. O access token (30 minutos) e o refresh token ficam em memória, são renovados automaticamente durante o uso e, ao sair da conta ou encerrar o aplicativo, são apagados e revogados no servidor. Fechar a janela não encerra a sessão quando o aplicativo continua na bandeja. O aplicativo não inclui credenciais de demonstração nem inicia o backend automaticamente.

## Distribuir

Para gerar uma pasta distribuível (requer o runtime .NET 10 Desktop na máquina de destino):

```powershell
dotnet publish src/InsightFlow.Desktop -c Release -r win-x64 --self-contained false -o publish
```

Para gerar um único `.exe` que não depende do runtime instalado (cerca de 50 MB), com o `appsettings.json` ao lado:

```powershell
dotnet publish src/InsightFlow.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

O ícone do executável, das janelas e da bandeja é [Assets/insightflow.ico](src/InsightFlow.Desktop/Assets/insightflow.ico), feito a partir do avatar da organização no GitHub.

### Instalador

O instalador é gerado com o [Inno Setup](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`) a partir de [installer/InsightFlow.iss](installer/InsightFlow.iss):

```powershell
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

O script publica o executável único, compila o instalador e grava `artifacts/installer/InsightFlow-Setup-<versão>.exe` (cerca de 45 MB). A versão vem de `Version` no `.csproj`. O instalador:

- instala por usuário, sem administrador, em `%LOCALAPPDATA%\Programs\InsightFlow`, com atalho no menu Iniciar e, se marcado, na área de trabalho;
- preserva o `appsettings.json` já existente numa atualização, para não perder a URL da API configurada; a exceção é o arquivo ainda com o padrão antigo dos primeiros instaladores (`http://localhost:8000`), trocado pelo que aponta para a produção;
- pede para encerrar o aplicativo se ele estiver aberto, inclusive na bandeja;
- na desinstalação, remove as preferências locais e a entrada de inicialização com o Windows.

O job `instalador` do workflow, só nos envios para a `main`, gera o mesmo instalador, instala, atualiza por cima (conferindo a troca do padrão antigo e a preservação de uma URL editada) e desinstala em modo silencioso, e o publica como artefato `InsightFlow-Setup`.

### Assinatura

A assinatura é opcional e depende de um certificado de assinatura de código (`.pfx`), que o projeto ainda não tem. Com o certificado, o mesmo script assina o executável, o desinstalador e o instalador (Authenticode, SHA-256, com carimbo de tempo):

```powershell
$env:INSIGHTFLOW_SIGN_PFX = 'C:\caminho\certificado.pfx'
$env:INSIGHTFLOW_SIGN_PASSWORD = '<senha do .pfx>'
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

No GitHub Actions, os segredos `SIGN_PFX_BASE64` (o `.pfx` em Base64) e `SIGN_PFX_PASSWORD` ligam a assinatura nos envios para `main` e `develop`; pull requests nunca recebem o certificado. Sem eles, o instalador sai sem assinatura: o SmartScreen avisa na primeira execução e o Smart App Control, quando ligado, bloqueia. Um certificado autoassinado serve para testar o fluxo, mas não remove esses avisos em outras máquinas. O `.pfx` não deve ser versionado (`*.pfx` está no `.gitignore`).

## Funcionalidades

1. Cadastrar uma conta ou entrar com e-mail e senha.
2. Buscar um ativo e adicioná-lo explicitamente à watchlist, que mostra preço, variação do dia e quantidade de sinais vigentes de cada ativo.
3. Selecionar um item para consultar cotação, gráfico de preço com indicadores, tabela OHLCV, indicadores técnicos e sinais.
4. Selecionar um sinal para consultar ocorrências e retornos do backtest, preservando resultados indisponíveis.
5. Criar, listar, editar, habilitar/desabilitar e excluir alertas de preço, com o preço atual e a distância até o alvo.
6. Alterar a preferência de notificação da watchlist, remover um item ou encerrar a sessão.
7. Consultar notificações (sinais, alertas disparados e resumo diário), marcar uma ou todas como lidas e receber avisos na bandeja do Windows, em tempo real pelo WebSocket da API, com a janela aberta ou fechada (veja [Segundo plano](#segundo-plano)).
8. Registrar operações simuladas, com a cotação do ativo como preço unitário sugerido e o valor total, e acompanhar posições com variação do dia, rentabilidade, comparação com CDI e Ibovespa, distribuição da carteira e o resumo diário gerado pelo servidor.
9. Gerar a análise por IA de um ativo e conversar com o Chat IA.
10. Em Configurações, escolher se o aplicativo continua na bandeja ao fechar a janela e se abre ao iniciar o Windows.

Toda tela que exibe texto de IA (análise, chat e resumo diário) mostra o aviso "Análise gerada por IA. Não constitui recomendação de investimento.": o texto enviado pela API quando existe, e o mesmo aviso fixo quando ela não o envia. A interface é informativa: não há chamadas para comprar ou vender, e verde e vermelho indicam apenas valores positivos e negativos.

O gráfico usa os registros OHLCV recebidos, em ordem cronológica: linha de fechamento ou candles, SMA 20, SMA 50, bandas de Bollinger e, no painel inferior, volume ou RSI (14). A roda do mouse e as teclas + e − aproximam; duplo clique ou Esc voltam à série inteira; as setas percorrem os registros. As séries dos indicadores são calculadas no desktop sobre o histórico recebido, com as mesmas fórmulas do backend, porque a API devolve só o valor mais recente de cada indicador (exibido na aba Indicadores). Ele preserva o horário e o fuso recebidos, exibe um ponto quando há um único registro e trata séries constantes, vazias e falhas de consulta. O eixo horizontal usa espaçamento uniforme entre registros, sem presumir uma frequência de pregão.

As tabelas guardam datas e valores tipados: clicar no título de uma coluna ordena pelo valor, e não pelo texto exibido.

### Segundo plano

Fechar a janela principal não encerra o aplicativo: ele continua na bandeja do Windows, com a sessão, o WebSocket e a consulta periódica ativos, e as notificações seguem chegando. O ícone da bandeja abre a janela (duplo clique) e tem um menu com "Abrir o InsightFlow", "Notificações" e "Encerrar o InsightFlow". Abrir o aplicativo de novo traz a janela que já está rodando, em vez de iniciar outra instância. Na primeira vez, um aviso explica que o aplicativo continua em segundo plano.

As duas opções ficam em Configurações e valem para o usuário do Windows (`%LOCALAPPDATA%\InsightFlow\preferences.json`, sem segredos): continuar em segundo plano ao fechar a janela, ligada por padrão, e abrir o InsightFlow ao iniciar o Windows, desligada por padrão (entrada em `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`).

Limite: com o aplicativo encerrado, ou antes de entrar na conta, não há entrega. A sessão fica só em memória, então depois de reiniciar o Windows é preciso entrar de novo; as notificações não lidas aparecem resumidas na entrada. Receber com o processo encerrado exigiria um canal de push do Windows no backend ou guardar a sessão no computador.

### Escala do Windows

As janelas acompanham a escala do Windows. Em 12/10/2026 as sete telas foram capturadas em 125%, 150% e 175% num monitor 1920 × 1080, no tamanho padrão e no mínimo, sem controles cortados:

- **Até 150%:** todas as telas cabem na área útil. Em janelas baixas, como as de 150% nesse monitor, a Visão geral entra em modo compacto: saem os textos de apoio e a lista de resultados vazia, e a watchlist e o gráfico ficam com a altura.
- **175%:** Entrar, Portfólio e Visão geral não cabem na área útil (1097 × 569 pixels lógicos). Elas mantêm o tamanho mínimo projetado e a janela ganha barras de rolagem, em vez de espremer os controles.
- **200%:** o Windows não oferece essa escala em monitores 1920 × 1080 (o máximo é 175%), então ela não foi exercitada de verdade. A área útil equivalente (960 × 492) foi simulada em 125%, com o mesmo comportamento de rolagem. Num monitor de resolução maior, 200% deixa uma área igual ou maior que a de 150% conferida aqui.

Não validado: mover a janela entre monitores de escalas diferentes com o aplicativo aberto.

## Estado da integração

O aplicativo usa endpoints reais, com erros e indisponibilidade apresentados na interface. O contrato foi conferido no código do backend e exercitado contra instâncias locais em Docker; a última conferência, em 09/10/2026, usou o backend em `a8bcb02` e cobriu watchlist, cotações, sinais, alertas, portfólio, resumo diário, análise e chat por IA e o aviso na bandeja com uma notificação recebida pelo WebSocket. Ainda não há um servidor mantido pela equipe para validar a integração. A IA depende de estar habilitada no servidor.

Ainda pendentes:

- notificações com o aplicativo encerrado (processo fechado ou antes de entrar), que dependem de um canal de push no backend ou de guardar a sessão no computador;
- histórico de dividendos e recuperação de senha, que dependem de contrato no backend;
- certificado de assinatura de código: o fluxo de assinatura está pronto, falta o certificado;
- validação em 200% num monitor que ofereça essa escala e ao mover a janela entre monitores de escalas diferentes.

O histórico de cada sincronização com o backend e com o cliente web está em [Integração com a API](docs/INTEGRACAO_API.md) e no [plano](docs/PLANO_DESKTOP.md).

## Estrutura

```text
InsightFlow.Desktop.sln
src/InsightFlow.Desktop/
  Api/                 Cliente HTTP, WebSocket de notificações e contratos JSON
  Configuration/       Configuração da API, preferências locais e inicialização com o Windows
  Forms/               Login/cadastro, janela principal, alertas, notificações, portfólio, chat e configurações
  Presentation/        Tema, gráfico, formatos das tabelas, aviso de IA e mensagens de erro
  Session/             Sessão mantida em memória
tests/InsightFlow.Desktop.Tests/
installer/               Script do Inno Setup, geração do instalador e assinatura
docs/
```

O aplicativo usa os componentes do .NET/Windows Forms, sem bibliotecas externas em runtime. A pasta `Api/` é o cliente do desktop; o backend fica em outro repositório.

## Validar

```powershell
dotnet test InsightFlow.Desktop.sln
```

Os testes usam xUnit com respostas HTTP controladas e não acessam contas reais. Cobrem os contratos da API (autenticação, sessão e sua renovação, WebSocket de notificações, busca, watchlist, análise técnica, alertas, notificações, portfólio, IA, erros, cancelamento e respostas sem corpo), a configuração da URL, as mensagens de erro, os formatos de apresentação, as preferências locais, a instância única e as telas (ordenação das tabelas, layout, modo compacto, rolagem em telas pequenas, segundo plano na bandeja e fluxos assíncronos). Os testes de preferências e de inicialização usam uma pasta temporária e uma chave de teste no registro do usuário, nunca as do aplicativo.

O workflow em [.github/workflows/ci.yml](.github/workflows/ci.yml) executa build e testes no Windows em cada pull request e nos envios para `main` e `develop`. O instalador e gerado, conferido e publicado como artefato (7 dias) apenas nos envios para a `main`.

## Documentação

- [Orientações de desenvolvimento](docs/ORIENTACOES_DESKTOP.md): escopo, documentos de origem, arquitetura, telas e padrões.
- [Integração com a API](docs/INTEGRACAO_API.md): endpoints, formatos, cuidados do cliente e histórico das sincronizações.
- [Plano de implementação e pendências](docs/PLANO_DESKTOP.md): sequência de entregas, critérios de aceite e decisões com a equipe.
- [Diretrizes de UX/UI](docs/UX_UI.md): identidade visual adaptada do cliente web e regras das telas.
