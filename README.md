# InsightFlow Desktop

Cliente Windows do sistema acadêmico de análise de ativos da B3 e criptomoedas. A documentação do projeto define C# com Windows Forms e consumo de uma API centralizada em Python/FastAPI.

Base inicial em Windows Forms com .NET 10, preparada para consumir a API documentada. O backend é um serviço separado e precisa estar disponível para autenticação e consultas.

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

A variável de ambiente tem precedência sobre [appsettings.json](src/InsightFlow.Desktop/appsettings.json). Fora de loopback, a URL deve usar HTTPS. Senhas são enviadas apenas no cadastro/login; o access token (30 minutos) e o refresh token ficam em memória, são renovados automaticamente durante o uso e, ao sair, são apagados e revogados no servidor. O aplicativo não inclui credenciais de demonstração nem inicia o backend automaticamente. Se a configuração for inválida, a mensagem de inicialização informa o motivo.

Para gerar uma pasta distribuível (requer o runtime .NET 10 Desktop na máquina de destino):

```powershell
dotnet publish src/InsightFlow.Desktop -c Release -r win-x64 --self-contained false -o publish
```

Para gerar um único `.exe` que não depende do runtime instalado (cerca de 50 MB), com o `appsettings.json` ao lado:

```powershell
dotnet publish src/InsightFlow.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

O ícone do executável, das janelas e da bandeja é [Assets/insightflow.ico](src/InsightFlow.Desktop/Assets/insightflow.ico), feito a partir do avatar da organização no GitHub. O executável gerado não é assinado; instalador e assinatura continuam pendentes.

## Primeiro fluxo implementado

1. Cadastrar uma conta ou entrar com e-mail e senha.
2. Buscar um ativo e adicioná-lo explicitamente à watchlist.
3. Selecionar um item para consultar cotação, gráfico de fechamento e volume, tabela OHLCV, indicadores técnicos e sinais.
4. Selecionar um sinal para consultar ocorrências e retornos do backtest, preservando resultados indisponíveis.
5. Criar, listar, editar, habilitar/desabilitar e excluir alertas de preço.
6. Alterar a preferência de notificação da watchlist, remover um item ou encerrar a sessão.
7. Consultar notificações (sinais, alertas disparados e resumo diário), marcar como lidas e receber avisos na bandeja do Windows, em tempo real pelo WebSocket da API, enquanto o app está aberto.
8. Registrar operações simuladas e acompanhar posições, rentabilidade, comparação com CDI/Ibovespa e distribuição da carteira.
9. Gerar a análise por IA de um ativo e conversar com o Chat IA, sempre com o aviso legal retornado pela API.

O fluxo usa endpoints reais, com erros e indisponibilidade apresentados na interface. O contrato foi conferido no código da branch `develop` do backend (commit `a175cb6`) e exercitado contra uma instância local desse backend em 30/09/2026; em 01/10/2026 o contrato foi reconferido sem diferenças. A integração com um servidor mantido pela equipe ainda precisa ser validada. A IA depende de estar habilitada no servidor.

As tabelas guardam datas e valores tipados: clicar no título de uma coluna ordena pelo valor, e não pelo texto exibido. As janelas acompanham a escala do Windows (conferido em 125%; 150% e 200% ainda sem validação).

Ainda pendentes: validação contra a API executada pela equipe, sinais e IA com dados reais, notificações com o app fechado, histórico de dividendos, instalador e assinatura do executável. Consulte o plano para as dependências de cada módulo.

## Estrutura

```text
InsightFlow.Desktop.sln
src/InsightFlow.Desktop/
  Api/                 Cliente HTTP e contratos JSON
  Configuration/       Configuração da API
  Forms/               Login/cadastro, janela principal, alertas, notificações, portfólio e chat
  Presentation/        Tema, gráfico, formatos das tabelas e mensagens de erro
  Session/             Sessão mantida em memória
tests/InsightFlow.Desktop.Tests/
docs/
```

O aplicativo usa os componentes do .NET/Windows Forms, sem bibliotecas externas em runtime. Os testes usam xUnit com respostas HTTP controladas; eles não acessam contas reais. Os contratos automatizados cobrem autenticação, sessão e sua renovação, WebSocket de notificações, busca, watchlist, análise técnica, alertas, notificações, portfólio, IA, erros, cancelamento e respostas sem corpo. Há também testes da configuração da URL, das mensagens de erro, dos formatos de apresentação e das telas (ordenação das tabelas, layout e fluxos assíncronos).

## Validar

```powershell
dotnet test InsightFlow.Desktop.sln
```

O workflow em [.github/workflows/desktop.yml](.github/workflows/desktop.yml) executa build e testes no Windows em cada pull request e nos envios para `main` e `develop`. Configuração das actions segue as referências oficiais de [setup-dotnet](https://github.com/actions/setup-dotnet) e [checkout](https://github.com/actions/checkout).

## Documentação

- [Orientações de desenvolvimento](docs/ORIENTACOES_DESKTOP.md): escopo, arquitetura, telas e padrões.
- [Integração com a API](docs/INTEGRACAO_API.md): endpoints documentados, formatos e cuidados do cliente.
- [Plano de implementação e pendências](docs/PLANO_DESKTOP.md): sequência de entregas, critérios de aceite e decisões com a equipe.
- [Diretrizes de UX/UI](docs/UX_UI.md): identidade visual adaptada do cliente web e regras das próximas telas.

## Base da análise

Foram lidos `Doc.pdf` (escopo e stack, agosto/2026), `documentacao-tecnica.docx` (versão 1.0, agosto/2026) e `api-reference.html` (15/09/2026, branch do backend `feature/alerta-service`). Os arquivos originais foram fornecidos pelo responsável pelo desktop e não foram copiados para o repositório.

A referência HTML descreve um estado do backend e declara não ser um contrato versionado. Funcionalidade documentada não significa integração testada nem disponibilidade em produção. As orientações distinguem requisitos, recomendações de implementação e pendências.

Repositório: [InsightF-AI/Insight-flow-desktop](https://github.com/InsightF-AI/Insight-flow-desktop). Na inspeção de 16/09/2026, estava vazio, sem código ou commits anteriores.

## Próximo marco

Validar o fluxo completo já estruturado com o backend da equipe, confirmando a URL da API e o contrato correspondente à versão em execução. O gráfico inicial usa os registros OHLCV recebidos, ordenados cronologicamente, com consulta por mouse e teclado. Candles, zoom e sobreposição de séries de indicadores ficam para uma etapa posterior.

Portfólio, IA e notificações foram integrados a partir do código do backend. A última sincronização, em 08/10/2026, cobre a `develop` até o commit `3ed5a8c` (refresh de sessão, logout remoto e WebSocket de notificações); ver [Integração com a API](docs/INTEGRACAO_API.md).

O gráfico preserva o horário/fuso recebido, exibe um ponto quando há somente um registro e trata séries constantes, vazias e falhas de consulta. O eixo horizontal representa registros com espaçamento uniforme; não presume uma frequência de pregão. A tabela mantém os valores exatos para consulta.
