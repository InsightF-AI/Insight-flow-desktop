# Estratégia de testes — desktop

A pirâmide tem três andares. **Base:** testes de unidade sobre código puro, sem janela e sem rede — validação de `ApiSettings` (URL, esquema, timeout), séries de histórico e indicadores calculados no cliente, e as preferências locais. **Meio:** testes de contrato do `InsightFlowApiClient` com um `RecordingHttpMessageHandler` no lugar da rede, conferindo rota, corpo, cabeçalho de autenticação, renovação de sessão e tradução de erro da API; mais os testes de interface, que montam os formulários em memória e verificam estado dos controles. **Topo:** operação ponta a ponta com a API no ar é manual, pelo roteiro de apresentação.

Cobertura da V0: contrato de autenticação, watchlist, ativos, sinais, alertas, carteira, notificações e IA; renovação de token; montagem das telas e das grades; cálculo de SMA, Bollinger e RSI sobre o histórico recebido; preferências locais. Nenhum teste abre janela visível nem acessa a rede.

Fica para depois: automação de interface de verdade (WinAppDriver ou Appium), teste do instalador além do ciclo instalar/desinstalar em silêncio que o CI já faz, e cobertura de código publicada.

TDD é o ciclo vermelho, verde, refatora: primeiro o teste que falha, depois o código mínimo que o faz passar, e só então a melhoria do desenho.

São **110 testes** em 10 arquivos, em `tests/InsightFlow.Desktop.Tests`. Rodar: `dotnet test InsightFlow.Desktop.sln --configuration Release`.
