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

A variável de ambiente tem precedência sobre [appsettings.json](src/InsightFlow.Desktop/appsettings.json). Fora de loopback, a URL deve usar HTTPS. Senhas são enviadas apenas no cadastro/login; o token fica em memória e é apagado ao sair. O aplicativo não inclui credenciais de demonstração nem inicia o backend automaticamente.

## Primeiro fluxo implementado

1. Cadastrar uma conta ou entrar com e-mail e senha.
2. Buscar um ativo e adicioná-lo explicitamente à watchlist.
3. Selecionar um item para consultar cotação e histórico OHLCV em tabela, escolhendo o período.
4. Alterar a preferência de notificação, remover um item ou encerrar a sessão.

O fluxo usa endpoints reais, com erros e indisponibilidade apresentados na interface. A integração com um servidor da equipe ainda precisa ser validada. A preferência de notificação pode ser salva; entrega de notificações depende de contrato adicional do backend.

Ainda pendentes: gráficos, indicadores/sinais/backtest na interface, CRUD de alertas de preço, entrega de notificações, portfólio, IA e empacotamento para distribuição. Consulte o plano para as dependências de cada módulo.

## Estrutura

```text
InsightFlow.Desktop.sln
src/InsightFlow.Desktop/
  Api/                 Cliente HTTP e contratos JSON
  Configuration/       Configuração da API
  Forms/               Login/cadastro e janela principal
  Presentation/        Apoio à apresentação
  Session/             Sessão mantida em memória
tests/InsightFlow.Desktop.Tests/
docs/
```

O aplicativo usa os componentes do .NET/Windows Forms, sem bibliotecas externas em runtime. Os testes usam xUnit com respostas HTTP controladas; eles não acessam contas reais.

## Validar

```powershell
dotnet test InsightFlow.Desktop.sln
```

O workflow em [.github/workflows/desktop.yml](.github/workflows/desktop.yml) prepara build e testes no Windows após o envio ao GitHub. Ele ainda não foi executado no serviço remoto. Configuração das actions segue as referências oficiais de [setup-dotnet](https://github.com/actions/setup-dotnet) e [checkout](https://github.com/actions/checkout).

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

Validar o primeiro fluxo com o backend da equipe, confirmando a URL da API e o contrato correspondente à versão em execução. Em seguida, implementar gráficos e a apresentação de indicadores, sinais e backtest.

Portfólio, IA e notificações continuam no escopo do projeto; sua integração depende de contratos que não aparecem na referência recebida.
