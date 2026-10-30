using System.Globalization;
using System.Net;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Configuration;
using InsightFlow.Desktop.Presentation;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Forms;

internal sealed partial class MainForm : AsyncForm
{
    // O WebSocket avisa das notificações na hora, mas não reenvia o que chegou com a conexão caída;
    // a consulta periódica cobre essas lacunas e os servidores sem o canal de tempo real.
    private const int NotificationPollMilliseconds = 120_000;
    private const string LongestPeriod = "5A";
    private readonly HashSet<Guid> _knownNotifications = [];
    private readonly InsightFlowApiClient _api;
    private readonly UserSession _session;
    private readonly NotificationStream? _notificationStream;
    // Sem preferências (testes e ferramentas), fechar a janela encerra o aplicativo e Configurações fica indisponível.
    private readonly UserPreferences? _preferences;
    private readonly WindowsStartup? _startup;
    private readonly int _sessionVersion;
    private readonly List<WatchlistItemResponse> _watchlist = [];
    // Cotação e sinais vigentes por ativo da watchlist; sobrevivem às remontagens da grade.
    private readonly Dictionary<Guid, WatchMarket> _market = [];
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _detailCancellation;
    private CancellationTokenSource? _backtestCancellation;
    private CancellationTokenSource? _analysisCancellation;
    private int _searchVersion;
    private int _detailVersion;
    private bool _bindingWatchlist;
    private bool _bindingSignals;
    private bool _watchBusy;
    private bool _endingSession;
    private bool _exiting;
    private bool _backgroundNoticeOnScreen;
    private bool _pollingNotifications;
    private bool _pollAgain;
    private bool _notificationsSeeded;
    private bool _dialogOpen;
    private FormWindowState _restoredState = FormWindowState.Normal;

    public bool LogoutRequested { get; private set; }
    public bool SessionExpired { get; private set; }
    // Revogação do login no servidor, iniciada ao sair; quem encerra o aplicativo pode aguardá-la por um instante.
    public Task Revocation { get; private set; } = Task.CompletedTask;
    private WatchlistItemResponse? SelectedItem => _watchGrid.CurrentRow?.Tag as WatchlistItemResponse;
    private bool IsCurrentSession => !IsClosing && _session.Version == _sessionVersion;

    public MainForm(InsightFlowApiClient api, UserSession session, Uri apiUrl, NotificationStream? notificationStream = null,
        UserPreferences? preferences = null, WindowsStartup? startup = null)
    {
        _api = api;
        _session = session;
        _notificationStream = notificationStream;
        _preferences = preferences;
        _startup = startup;
        _sessionVersion = session.Version;
        BuildLayout(apiUrl);
        _searchButton.Click += (_, _) => Search();
        _search.TextChanged += (_, _) => InvalidateSearch();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            Search();
        };
        _searchResults.SelectedIndexChanged += (_, _) => UpdateActions();
        _addButton.Click += (_, _) => AddSelectedAsset();
        _refreshWatch.Click += (_, _) => LoadWatchlist();
        _removeButton.Click += (_, _) => RemoveSelectedAsset();
        _saveNotification.Click += (_, _) => SaveNotificationPreference();
        _notify.CheckedChanged += (_, _) => UpdateActions();
        _watchGrid.SelectionChanged += (_, _) =>
        {
            if (_bindingWatchlist) return;
            _notify.Checked = SelectedItem?.Notificar ?? false;
            UpdateActions();
            LoadDetails();
        };
        _period.SelectedIndexChanged += (_, _) => LoadDetails();
        _refreshDetails.Click += (_, _) => LoadDetails();
        _signals.SelectionChanged += (_, _) =>
        {
            if (!_bindingSignals) LoadSelectedBacktest();
        };
        _checkConnection.Click += (_, _) => CheckConnection();
        _analysisButton.Click += (_, _) => LoadAnalysis();
        _notificationTimer.Tick += (_, _) => PollNotifications();
        _tray.BalloonTipClicked += (_, _) =>
        {
            // O aviso de segundo plano só traz a janela de volta; os demais levam às notificações.
            if (_backgroundNoticeOnScreen) ShowFromTray();
            else OpenNotificationsFromTray();
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
        Resize += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized) _restoredState = WindowState;
        };
        Load += (_, _) => ApplyDensity();
        ClientSizeChanged += (_, _) => ApplyDensity();
        Shown += (_, _) =>
        {
            CheckConnection();
            LoadWatchlist();
            _tray.Visible = true;
            PollNotifications();
            _notificationTimer.Start();
            ListenForNotifications();
        };
        FormClosed += (_, _) =>
        {
            StopNotifications();
            RevokeSession();
            _watchlist.Clear();
            _market.Clear();
            _searchResults.Items.Clear();
            _watchGrid.Rows.Clear();
            _history.Rows.Clear();
            _indicators.Rows.Clear();
            _signals.Rows.Clear();
            _backtest.Rows.Clear();
        };
        UpdateActions();
        CompleteLayout();
    }

    private void CheckConnection()
    {
        if (!_checkConnection.Enabled || !IsCurrentSession) return;
        _checkConnection.Enabled = false;
        SetStatus(_connection, "Verificando API…");
        Run(async cancellationToken =>
        {
            try
            {
                await _api.CheckHealthAsync(cancellationToken);
                if (IsCurrentSession) SetStatus(_connection, "API disponível");
            }
            finally
            {
                if (IsCurrentSession) _checkConnection.Enabled = true;
            }
        }, exception => SetStatus(_connection, UiErrors.Message(exception), true));
    }

    private void InvalidateSearch()
    {
        _searchVersion++;
        _searchCancellation?.Cancel();
        _searchResults.Items.Clear();
        SetStatus(_searchStatus, "Pesquise um ativo para adicioná-lo, ou busque sem termo para ver o catálogo.");
        ApplyDensity();
        UpdateActions();
    }

    private void Search()
    {
        var term = _search.Text.Trim();
        if (!IsCurrentSession) return;
        InvalidateSearch();
        var version = _searchVersion;
        SetStatus(_searchStatus, term.Length == 0 ? "Carregando o catálogo de ativos…" : "Buscando ativos…");
        Run(async lifetime =>
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _searchCancellation = request;
            try
            {
                var result = term.Length == 0
                    ? (await _api.GetCatalogAsync(request.Token)).Select(ToSearchAsset).ToList()
                    : await _api.SearchAssetsAsync(term, request.Token);
                if (!IsCurrentSession || version != _searchVersion || request.IsCancellationRequested) return;
                foreach (var asset in result) _searchResults.Items.Add(new SearchResult(asset));
                ApplyDensity();
                SetStatus(_searchStatus, result.Count == 0 ? "Nenhum ativo encontrado." : $"{result.Count} resultado(s). Selecione e adicione à watchlist.");
                if (result.Count > 0) _searchResults.SelectedIndex = 0;
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (version == _searchVersion) HandleError(exception, _searchStatus);
            }
            finally
            {
                if (ReferenceEquals(_searchCancellation, request)) _searchCancellation = null;
            }
        }, exception => HandleError(exception, _searchStatus));
    }

    private void LoadWatchlist()
    {
        if (_watchBusy || !IsCurrentSession) return;
        SetWatchBusy(true);
        SetStatus(_watchStatus, "Atualizando watchlist…");
        Run(async cancellationToken =>
        {
            try
            {
                var selected = SelectedItem?.Ativo.Id;
                var items = await _api.GetWatchlistAsync(cancellationToken);
                if (!IsCurrentSession) return;
                _watchlist.Clear();
                _watchlist.AddRange(items);
                BindWatchlist(selected);
                LoadWatchMarket(items);
                SetStatus(_watchStatus, items.Count == 0 ? "Sua watchlist está vazia. Adicione um ativo acima." : $"{items.Count} ativo(s) acompanhado(s).");
            }
            finally { if (IsCurrentSession) SetWatchBusy(false); }
        }, exception => HandleError(exception, _watchStatus));
    }

    private void AddSelectedAsset()
    {
        if (_watchBusy || !IsCurrentSession || _searchResults.SelectedItem is not SearchResult selected) return;
        SetWatchBusy(true);
        SetStatus(_watchStatus, $"Adicionando {selected.Asset.Ticker}…");
        Run(async cancellationToken =>
        {
            try
            {
                var item = await _api.AddToWatchlistAsync(selected.Asset.Ticker, cancellationToken);
                if (!IsCurrentSession) return;
                _watchlist.RemoveAll(existing => existing.Ativo.Id == item.Ativo.Id);
                _watchlist.Add(item);
                BindWatchlist(item.Ativo.Id);
                SetStatus(_watchStatus, $"{item.Ativo.Ticker} adicionado à watchlist.");
            }
            finally { if (IsCurrentSession) SetWatchBusy(false); }
        }, exception => HandleError(exception, _watchStatus));
    }

    private void RemoveSelectedAsset()
    {
        var item = SelectedItem;
        if (_watchBusy || !IsCurrentSession || item is null) return;
        SetWatchBusy(true);
        SetStatus(_watchStatus, $"Removendo {item.Ativo.Ticker}…");
        Run(async cancellationToken =>
        {
            try
            {
                await _api.RemoveFromWatchlistAsync(item.Ativo.Id, cancellationToken);
                if (!IsCurrentSession) return;
                _watchlist.RemoveAll(existing => existing.Ativo.Id == item.Ativo.Id);
                _market.Remove(item.Ativo.Id);
                BindWatchlist(null);
                SetStatus(_watchStatus, _watchlist.Count == 0 ? "Sua watchlist está vazia." : $"{item.Ativo.Ticker} removido.");
            }
            finally { if (IsCurrentSession) SetWatchBusy(false); }
        }, exception => HandleError(exception, _watchStatus));
    }

    private void SaveNotificationPreference()
    {
        var item = SelectedItem;
        if (_watchBusy || !IsCurrentSession || item is null) return;
        var enabled = _notify.Checked;
        SetWatchBusy(true);
        SetStatus(_watchStatus, "Salvando preferência…");
        Run(async cancellationToken =>
        {
            try
            {
                var updated = await _api.SetNotificationsAsync(item.Ativo.Id, enabled, cancellationToken);
                if (!IsCurrentSession) return;
                var index = _watchlist.FindIndex(existing => existing.Ativo.Id == item.Ativo.Id);
                if (index >= 0) _watchlist[index] = updated;
                // A preferência não muda cotação nem histórico: a linha é atualizada sem recarregar os detalhes do ativo.
                var row = _watchGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(candidate => ReferenceEquals(candidate.Tag, item));
                if (row is not null) row.Tag = updated;
                if (SelectedItem?.Ativo.Id == updated.Ativo.Id) _notify.Checked = updated.Notificar;
                SetStatus(_watchStatus, enabled
                    ? "Preferência salva. Sinais deste ativo aparecerão em Notificações."
                    : "Preferência salva. Este ativo não gerará notificações de sinais.");
            }
            finally { if (IsCurrentSession) SetWatchBusy(false); }
        }, exception => HandleError(exception, _watchStatus));
    }

    private void BindWatchlist(Guid? preferredId)
    {
        _bindingWatchlist = true;
        try
        {
            _watchGrid.Rows.Clear();
            foreach (var item in _watchlist)
            {
                var row = _watchGrid.Rows[_watchGrid.Rows.Add(item.Ativo.Ticker, item.Ativo.Nome)];
                row.Tag = item;
                row.SetFormat(GridCells.Money(item.Ativo.Moeda), "Preço");
                ShowWatchMarket(row);
            }
            if (_watchGrid.Rows.Count > 0)
            {
                var selected = _watchGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row => ((WatchlistItemResponse)row.Tag!).Ativo.Id == preferredId) ?? _watchGrid.Rows[0];
                _watchGrid.CurrentCell = selected.Cells[0];
            }
        }
        finally { _bindingWatchlist = false; }
        _notify.Checked = SelectedItem?.Notificar ?? false;
        UpdateActions();
        LoadDetails();
    }

    // Preço, variação do dia e sinais vigentes de cada ativo, como na watchlist do cliente web.
    private void LoadWatchMarket(IReadOnlyList<WatchlistItemResponse> items)
    {
        if (items.Count == 0 || !IsCurrentSession) return;
        Run(cancellationToken => Task.WhenAll(items.Select(item => LoadWatchMarketAsync(item.Ativo.Id, cancellationToken))),
            exception => HandleError(exception, _watchStatus));
    }

    private async Task LoadWatchMarketAsync(Guid assetId, CancellationToken cancellationToken)
    {
        // Cotação e sinais falham em separado: a fonte de mercado indisponível não esconde os sinais já guardados.
        var quoteTask = _api.GetQuoteAsync(assetId, cancellationToken).OrNullAsync(cancellationToken);
        var signalsTask = _api.GetSignalsAsync(assetId, cancellationToken).OrNullAsync(cancellationToken);
        var quote = await quoteTask;
        var signals = await signalsTask;
        if (!IsCurrentSession) return;
        // Uma consulta que falha deixa a célula vazia em vez de manter o valor anterior como se fosse atual.
        RememberMarket(assetId, _ => new WatchMarket(quote, signals?.Count(signal => signal.DataDesativacao is null)));
    }

    private void RememberMarket(Guid assetId, Func<WatchMarket, WatchMarket> update)
    {
        if (_watchlist.All(item => item.Ativo.Id != assetId)) return;
        _market[assetId] = update(_market.GetValueOrDefault(assetId) ?? new WatchMarket());
        var row = _watchGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(candidate => ((WatchlistItemResponse)candidate.Tag!).Ativo.Id == assetId);
        if (row is not null) ShowWatchMarket(row);
    }

    private void ShowWatchMarket(DataGridViewRow row)
    {
        var market = _market.GetValueOrDefault(((WatchlistItemResponse)row.Tag!).Ativo.Id);
        row.Cells["Preço"].Value = market?.Quote?.Preco;
        var change = row.Cells["Var. dia"];
        change.Value = market?.Quote?.VariacaoPercentual;
        change.Style.ForeColor = change.Style.SelectionForeColor = market?.Quote?.VariacaoPercentual switch
        {
            > 0 => UiTheme.Up,
            < 0 => UiTheme.Down,
            _ => Color.Empty
        };
        var signals = row.Cells["Sinais"];
        signals.Value = market?.ActiveSignals;
        // Âmbar marca contexto de sinal, sem sugerir compra ou venda.
        signals.Style.ForeColor = signals.Style.SelectionForeColor = market?.ActiveSignals > 0 ? UiTheme.Amber : Color.Empty;
    }

    private void LoadDetails()
    {
        _detailCancellation?.Cancel();
        _backtestCancellation?.Cancel();
        _analysisCancellation?.Cancel();
        var version = ++_detailVersion;
        var item = SelectedItem;
        ResetDetails(item);
        if (item is null || !IsCurrentSession) return;
        var period = (_period.SelectedItem as HistoryPeriod)?.Value ?? "1M";
        SetStatus(_quoteStatus, "Consultando cotação…");
        SetStatus(_historyStatus, "Carregando histórico…");
        _historyChart.ShowMessage("Carregando histórico…");
        SetStatus(_indicatorStatus, "Carregando indicadores…");
        SetStatus(_signalStatus, "Carregando sinais…");
        Run(async lifetime =>
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _detailCancellation = request;
            try
            {
                await Task.WhenAll(
                    LoadQuoteAsync(item, version, request.Token),
                    LoadHistoryAsync(item, period, version, request.Token),
                    LoadIndicatorsAsync(item, version, request.Token),
                    LoadSignalsAsync(item, version, request.Token));
            }
            finally
            {
                if (ReferenceEquals(_detailCancellation, request)) _detailCancellation = null;
            }
        }, exception => HandleError(exception, _historyStatus));
    }

    private bool IsCurrentDetail(int version, CancellationToken cancellationToken) =>
        IsCurrentSession && version == _detailVersion && !cancellationToken.IsCancellationRequested;

    private async Task LoadQuoteAsync(WatchlistItemResponse item, int version, CancellationToken cancellationToken)
    {
        try
        {
            var quote = await _api.GetQuoteAsync(item.Ativo.Id, cancellationToken);
            if (!IsCurrentDetail(version, cancellationToken)) return;
            var currency = item.Ativo.Moeda;
            _quotePrice.Text = $"{currency} {Number(quote.Preco)}";
            _quoteChange.Text = $"Variação: {quote.Variacao:+0.########;-0.########;0}  ({quote.VariacaoPercentual:+0.##;-0.##;0}%)";
            _quoteChange.ForeColor = quote.Variacao < 0 ? UiTheme.Down : quote.Variacao > 0 ? UiTheme.Up : UiTheme.Ink;
            _quoteRange.Text = $"Mín. {currency} {Number(quote.MinimaDia)}  /  Máx. {currency} {Number(quote.MaximaDia)}";
            _quoteVolume.Text = $"Volume: {Number(quote.Volume)}";
            RememberMarket(item.Ativo.Id, market => market with { Quote = quote });
            SetStatus(_quoteStatus, $"Consulta realizada às {DateTimeOffset.Now:HH:mm:ss}. Horário de mercado não informado.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentDetail(version, cancellationToken)) HandleError(exception, _quoteStatus);
        }
    }

    private async Task LoadHistoryAsync(WatchlistItemResponse item, string period, int version, CancellationToken cancellationToken)
    {
        try
        {
            // O histórico mais longo dá às médias do gráfico os registros anteriores ao período exibido. Sem ele,
            // os indicadores usam só o que está na tela. "1D" tem um único ponto e "5A" já é o mais longo.
            var lookbackRequest = period is "1D" or LongestPeriod
                ? null
                : _api.GetHistoryAsync(item.Ativo.Id, LongestPeriod, cancellationToken).OrNullAsync(cancellationToken);
            var points = await _api.GetHistoryAsync(item.Ativo.Id, period, cancellationToken);
            var lookback = lookbackRequest is null ? null : await lookbackRequest;
            if (!IsCurrentDetail(version, cancellationToken)) return;
            _history.Rows.Clear();
            _historyChart.SetData(points, item.Ativo.Moeda, lookback);
            foreach (var point in points.OrderByDescending(point => point.Data))
            {
                var index = _history.Rows.Add(point.Data.DateTime, point.Abertura, point.Maxima, point.Minima, point.Fechamento, point.Volume);
                _history.Rows[index].Cells[0].ToolTipText = point.Data.ToString("O");
            }
            SetStatus(_historyStatus, points.Count == 0 ? "Sem histórico para este período." : $"{points.Count} registro(s) · Valores em {item.Ativo.Moeda} · Datas conforme recebidas da API.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentDetail(version, cancellationToken))
            {
                _historyChart.ShowMessage(UiErrors.Message(exception));
                HandleError(exception, _historyStatus);
            }
        }
    }

    private async Task LoadIndicatorsAsync(WatchlistItemResponse item, int version, CancellationToken cancellationToken)
    {
        try
        {
            var indicators = await _api.GetIndicatorsAsync(item.Ativo.Id, cancellationToken);
            if (!IsCurrentDetail(version, cancellationToken)) return;
            _indicators.Rows.Clear();
            foreach (var indicator in indicators)
            {
                _indicators.Rows.Add(IndicatorLabel(indicator.Tipo), indicator.Valor, indicator.DataCalculo.LocalDateTime,
                    JsonText.Summary(indicator.Parametros), JsonText.Summary(indicator.ValoresAuxiliares));
            }
            SetStatus(_indicatorStatus, indicators.Count == 0
                ? "Nenhum indicador disponível para este ativo."
                : $"{indicators.Count} indicador(es) recebido(s) da API.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentDetail(version, cancellationToken)) HandleError(exception, _indicatorStatus);
        }
    }

    private async Task LoadSignalsAsync(WatchlistItemResponse item, int version, CancellationToken cancellationToken)
    {
        try
        {
            var signals = await _api.GetSignalsAsync(item.Ativo.Id, cancellationToken);
            if (!IsCurrentDetail(version, cancellationToken)) return;
            _bindingSignals = true;
            try
            {
                _signals.Rows.Clear();
                foreach (var signal in signals)
                {
                    var index = _signals.Rows.Add(signal.RegraNome,
                        signal.DataDesativacao is null ? "Ativo" : "Desativado",
                        signal.DataAtivacao.LocalDateTime, signal.RegraDescricao, JsonText.Summary(signal.Contexto));
                    _signals.Rows[index].Tag = signal;
                }
                if (_signals.Rows.Count > 0) _signals.CurrentCell = _signals.Rows[0].Cells[0];
            }
            finally
            {
                _bindingSignals = false;
            }
            RememberMarket(item.Ativo.Id, market => market with { ActiveSignals = signals.Count(signal => signal.DataDesativacao is null) });
            SetStatus(_signalStatus, signals.Count == 0 ? "Nenhum sinal recente para este ativo." : $"{signals.Count} sinal(is) encontrado(s).");
            LoadSelectedBacktest();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (IsCurrentDetail(version, cancellationToken)) HandleError(exception, _signalStatus);
        }
    }

    private void LoadSelectedBacktest()
    {
        _backtestCancellation?.Cancel();
        _backtest.Rows.Clear();
        var item = SelectedItem;
        var signal = _signals.CurrentRow?.Tag as SignalResponse;
        if (item is null || signal is null || !IsCurrentSession)
        {
            SetStatus(_backtestStatus, "Selecione um sinal para consultar o backtest.");
            return;
        }

        var detailVersion = _detailVersion;
        SetStatus(_backtestStatus, $"Consultando backtest de {signal.RegraNome}…");
        Run(async lifetime =>
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _backtestCancellation = request;
            try
            {
                var result = await _api.GetBacktestAsync(item.Ativo.Id, signal.RegraId, request.Token);
                if (!IsCurrentDetail(detailVersion, request.Token) || !ReferenceEquals(_signals.CurrentRow?.Tag, signal)) return;
                _backtest.Rows.Add(result.TotalOcorrencias, NullablePercent(result.RetornoMedio5Pregoes),
                    NullablePercent(result.RetornoMedio20Pregoes), NullablePercent(result.RetornoMedio60Pregoes));
                SetStatus(_backtestStatus, "Retorno médio após cada ocorrência, em %. Indisponível significa histórico insuficiente para a janela.");
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (IsCurrentDetail(detailVersion, request.Token) && ReferenceEquals(_signals.CurrentRow?.Tag, signal))
                    HandleError(exception, _backtestStatus);
            }
            finally
            {
                if (ReferenceEquals(_backtestCancellation, request)) _backtestCancellation = null;
            }
        }, exception => HandleError(exception, _backtestStatus));
    }

    private void ResetDetails(WatchlistItemResponse? item)
    {
        _assetTitle.Text = item?.Ativo.Ticker ?? "Selecione um ativo";
        _assetDescription.Text = item is null ? "Adicione ou selecione um ativo na sua watchlist." : $"{item.Ativo.Nome} · {item.Ativo.Tipo} · {item.Ativo.Moeda}";
        _quotePrice.Text = "—";
        _quoteChange.Text = "Variação: —";
        _quoteChange.ForeColor = UiTheme.Ink;
        _quoteRange.Text = "Mínima / máxima do dia: —";
        _quoteVolume.Text = "Volume: —";
        SetStatus(_quoteStatus, "");
        _history.Rows.Clear();
        _historyChart.ShowMessage("Selecione um ativo para consultar o histórico.");
        SetStatus(_historyStatus, "Selecione um ativo da watchlist para consultar o histórico.");
        _indicators.Rows.Clear();
        _signals.Rows.Clear();
        _backtest.Rows.Clear();
        SetStatus(_indicatorStatus, "Selecione um ativo para consultar os indicadores.");
        SetStatus(_signalStatus, "Selecione um ativo para consultar os sinais.");
        SetStatus(_backtestStatus, "Selecione um sinal para consultar o backtest.");
        _analysisText.Clear();
        _analysisNotice.Text = "";
        SetStatus(_analysisStatus, item is null
            ? "Selecione um ativo para gerar a análise."
            : $"Gere uma análise de {item.Ativo.Ticker} quando quiser. Cada geração consome cota da IA no servidor.");
        UiTheme.SetButtonEnabled(_analysisButton, item is not null, true);
    }

    private void LoadAnalysis()
    {
        var item = SelectedItem;
        if (item is null || !IsCurrentSession || !_analysisButton.Enabled) return;
        _analysisCancellation?.Cancel();
        var detailVersion = _detailVersion;
        UiTheme.SetButtonEnabled(_analysisButton, false, true);
        _analysisText.Clear();
        _analysisNotice.Text = "";
        SetStatus(_analysisStatus, $"Gerando análise de {item.Ativo.Ticker}… Pode levar até 1 minuto.");
        Run(async lifetime =>
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _analysisCancellation = request;
            try
            {
                var analysis = await _api.GetAiAnalysisAsync(item.Ativo.Id, request.Token);
                if (!IsCurrentDetail(detailVersion, request.Token)) return;
                _analysisText.Text = analysis.Texto.ReplaceLineEndings("\r\n");
                _analysisNotice.Text = AiNotice.OrDefault(analysis.AvisoLegal);
                if (analysis.Desatualizada)
                {
                    SetStatus(_analysisStatus, "A IA está indisponível no momento. Esta é a última análise guardada, de " +
                                               $"{analysis.GeradoEm.LocalDateTime:dd/MM/yyyy HH:mm} ({analysis.Modelo}); pode estar desatualizada.");
                    _analysisStatus.ForeColor = UiTheme.Amber;
                }
                else
                {
                    SetStatus(_analysisStatus, $"Gerada em {analysis.GeradoEm.LocalDateTime:dd/MM/yyyy HH:mm} por {analysis.Modelo}" +
                                               (analysis.EmCache ? " · reaproveitada pelo servidor (dados sem alteração)." : "."));
                }
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (!IsCurrentDetail(detailVersion, request.Token)) return;
                if (exception is ApiException { StatusCode: HttpStatusCode.Unauthorized }) HandleError(exception, _analysisStatus);
                else SetStatus(_analysisStatus, UiErrors.AiMessage(exception), true);
            }
            finally
            {
                if (ReferenceEquals(_analysisCancellation, request)) _analysisCancellation = null;
                if (IsCurrentDetail(detailVersion, CancellationToken.None)) UiTheme.SetButtonEnabled(_analysisButton, true, true);
            }
        }, exception => HandleError(exception, _analysisStatus));
    }

    private void ListenForNotifications()
    {
        if (_notificationStream is not { } stream || SynchronizationContext.Current is not { } ui) return;
        // A conexão vive fora da thread de interface; cada aviso volta a ela para consultar as não lidas.
        Run(cancellationToken => Task.Run(
            () => stream.RunAsync(() => ui.Post(_ => PollNotifications(), null), cancellationToken), cancellationToken), _ => { });
    }

    private void PollNotifications()
    {
        if (!IsCurrentSession) return;
        if (_pollingNotifications)
        {
            // Um aviso que chega durante uma consulta pede outra em seguida, para não perder a notificação nova.
            _pollAgain = true;
            return;
        }
        _pollingNotifications = true;
        Run(async cancellationToken =>
        {
            try
            {
                var unread = await _api.GetNotificationsAsync(onlyUnread: true, cancellationToken);
                if (!IsCurrentSession) return;
                UpdateUnreadCount(unread.Count);
                var fresh = unread.Where(notification => _knownNotifications.Add(notification.Id)).ToArray();
                if (!_notificationsSeeded)
                {
                    // Na primeira consulta da sessão, um único resumo evita uma rajada de avisos antigos.
                    _notificationsSeeded = true;
                    if (unread.Count > 0)
                        ShowBalloon("Notificações não lidas", $"Você tem {unread.Count} notificação(ões) não lida(s).");
                }
                else if (fresh.Length == 1)
                {
                    ShowBalloon(NotificationsForm.TypeLabel(fresh[0].Tipo), fresh[0].Mensagem);
                }
                else if (fresh.Length > 1)
                {
                    ShowBalloon("Novas notificações", $"{fresh.Length} novas notificações. A mais recente: {fresh.MaxBy(item => item.CriadoEm)!.Mensagem}");
                }
            }
            catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Com um módulo aberto, a tela ativa encerra a sessão na próxima consulta; a principal não fecha por baixo dela.
                if (IsCurrentSession && !_dialogOpen) EndSession(true);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Falhas da consulta em segundo plano não interrompem o uso; a próxima rodada tenta de novo.
            }
            finally
            {
                _pollingNotifications = false;
                if (_pollAgain)
                {
                    _pollAgain = false;
                    PollNotifications();
                }
            }
        }, _ => { });
    }

    private void UpdateUnreadCount(int count)
    {
        _notificationsNav.Text = count == 0 ? "  Notificações" : $"  Notificações ({count})";
        _notificationsNav.ForeColor = count == 0 ? UiTheme.InkMuted : UiTheme.Amber;
        _tray.Text = count == 0 ? "InsightFlow" : $"InsightFlow — {count} não lida(s)";
    }

    private void ShowBalloon(string title, string text)
    {
        if (!_tray.Visible) return;
        _backgroundNoticeOnScreen = false;
        _tray.ShowBalloonTip(8000, title, text.Length <= 240 ? text : text[..237] + "…", ToolTipIcon.Info);
    }

    private void StopNotifications()
    {
        _notificationTimer.Stop();
        _tray.Visible = false;
        _knownNotifications.Clear();
    }

    private void SetWatchBusy(bool busy)
    {
        _watchBusy = busy;
        UpdateActions();
    }

    private void UpdateActions()
    {
        var selected = SelectedItem;
        UiTheme.SetButtonEnabled(_addButton, !_watchBusy && _searchResults.SelectedItem is SearchResult, true);
        UiTheme.SetButtonEnabled(_refreshWatch, !_watchBusy);
        _watchGrid.Enabled = !_watchBusy;
        UiTheme.SetButtonEnabled(_removeButton, !_watchBusy && selected is not null);
        _notify.Enabled = !_watchBusy && selected is not null;
        UiTheme.SetButtonEnabled(_saveNotification,
            !_watchBusy && selected is not null && selected.Notificar != _notify.Checked);
        _period.Enabled = _refreshDetails.Enabled = selected is not null;
    }

    private void HandleError(Exception exception, Label target)
    {
        if (!IsCurrentSession) return;
        if (exception is ApiException { StatusCode: HttpStatusCode.Unauthorized })
        {
            EndSession(true);
            return;
        }
        SetStatus(target, UiErrors.Message(exception), true);
    }

    private void EndSession(bool expired)
    {
        if (_endingSession) return;
        _endingSession = true;
        _exiting = true;
        SessionExpired = expired;
        LogoutRequested = true;
        StopNotifications();
        // Numa sessão expirada o servidor já recusou o refresh token: não há o que revogar.
        if (expired) _session.Clear();
        else RevokeSession();
        _searchCancellation?.Cancel();
        _detailCancellation?.Cancel();
        _backtestCancellation?.Cancel();
        _analysisCancellation?.Cancel();
        _watchlist.Clear();
        _market.Clear();
        _searchResults.Items.Clear();
        _watchGrid.Rows.Clear();
        _history.Rows.Clear();
        _indicators.Rows.Clear();
        _signals.Rows.Clear();
        _backtest.Rows.Clear();
        Close();
    }

    // Sair apaga a sessão local na hora; a revogação no servidor segue em segundo plano e não impede a saída se falhar.
    private void RevokeSession()
    {
        var refreshToken = _session.RefreshToken;
        _session.Clear();
        if (refreshToken is not null) Revocation = RevokeAsync(refreshToken);
    }

    private async Task RevokeAsync(string refreshToken)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _api.LogoutAsync(refreshToken, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or OperationCanceledException)
        {
            // Sem resposta do servidor, o refresh token descartado simplesmente expira.
        }
    }

    private static string Number(decimal value) => value.ToString("#,0.########", CultureInfo.CurrentCulture);

    // O backend calcula (preço futuro / preço base − 1) × 100: o valor já está em pontos percentuais.
    private static string NullablePercent(decimal? value) =>
        value is null ? "Indisponível" : value.Value.ToString("+#,0.##;-#,0.##;0", CultureInfo.CurrentCulture) + "%";

    private static string IndicatorLabel(string type) => type switch
    {
        "BOLLINGER" => "Bollinger",
        "VOLUME_RELATIVO" => "Volume relativo",
        _ => type
    };

    private void OpenAlerts()
    {
        if (!IsCurrentSession || _dialogOpen) return;
        using var form = new AlertsForm(_api, _session);
        ShowModule(form, () => form.SessionExpired);
    }

    private void OpenNotifications()
    {
        if (!IsCurrentSession || _dialogOpen) return;
        var tickers = _watchlist.ToDictionary(item => item.Ativo.Id, item => item.Ativo.Ticker);
        using var form = new NotificationsForm(_api, _session, tickers);
        ShowModule(form, () => form.SessionExpired);
        // Leituras feitas na tela atualizam o contador sem esperar a próxima rodada.
        PollNotifications();
    }

    private void OpenNotificationsFromTray()
    {
        // O aviso pode ser clicado com a janela minimizada ou na bandeja; a tela de notificações precisa de uma janela visível por trás.
        ShowFromTray();
        OpenNotifications();
    }

    // Fechar a janela não encerra o aplicativo quando a preferência pede a bandeja: a sessão, o WebSocket e a
    // consulta periódica continuam, e as notificações seguem chegando.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_exiting && _tray.Visible && _preferences is { KeepRunningInTray: true })
        {
            e.Cancel = true;
            HideToTray();
            return;
        }
        base.OnFormClosing(e);
    }

    private void HideToTray()
    {
        Hide();
        if (_preferences is not { BackgroundNoticeShown: false } preferences) return;
        ShowBalloon("InsightFlow continua em segundo plano",
            "As notificações continuam chegando. Para encerrar, clique com o botão direito no ícone da bandeja.");
        _backgroundNoticeOnScreen = true;
        preferences.BackgroundNoticeShown = true;
        try
        {
            preferences.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Sem gravar a preferência, o aviso volta a aparecer na próxima execução.
        }
    }

    // Traz a janela de volta da bandeja ou da barra de tarefas; também atende a quem abre o aplicativo de novo.
    internal void ShowFromTray()
    {
        if (IsClosing) return;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = _restoredState;
        // Com um módulo aberto, é ele que recebe o foco.
        (OwnedForms.FirstOrDefault(form => form.Visible) ?? this).Activate();
    }

    private void Exit()
    {
        if (_dialogOpen) return;
        _exiting = true;
        Close();
    }

    private void OpenSettings()
    {
        if (!IsCurrentSession || _dialogOpen || _preferences is null || _startup is null) return;
        using var form = new SettingsForm(_preferences, _startup);
        ShowModule(form, () => false);
    }

    private void OpenPortfolio()
    {
        if (!IsCurrentSession || _dialogOpen) return;
        using var form = new PortfolioForm(_api, _session, _watchlist.Select(item => item.Ativo));
        ShowModule(form, () => form.SessionExpired);
    }

    private void OpenChat()
    {
        if (!IsCurrentSession || _dialogOpen) return;
        using var form = new ChatForm(_api, _session);
        ShowModule(form, () => form.SessionExpired);
    }

    private void ShowModule(Form form, Func<bool> sessionExpired)
    {
        _dialogOpen = true;
        try
        {
            form.ShowDialog(this);
        }
        finally
        {
            _dialogOpen = false;
        }
        if (sessionExpired()) EndSession(true);
    }

    private static void SetStatus(Label label, string text, bool error = false)
    {
        label.Text = text;
        label.ForeColor = error ? UiTheme.Error : UiTheme.InkMuted;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notificationTimer.Dispose();
            _tray.Dispose();
            _trayMenu.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record WatchMarket(QuoteResponse? Quote = null, int? ActiveSignals = null);

    private static SearchAssetResponse ToSearchAsset(AssetResponse asset) =>
        new() { Ticker = asset.Ticker, Nome = asset.Nome, Tipo = asset.Tipo, Moeda = asset.Moeda, Setor = asset.Setor };

    private sealed record SearchResult(SearchAssetResponse Asset)
    {
        public string Display => $"{Asset.Ticker} — {Asset.Nome}";
    }
}
