using System.Globalization;
using System.Net;
using System.Text.Json;
using InsightFlow.Desktop.Api;
using InsightFlow.Desktop.Api.Contracts;
using InsightFlow.Desktop.Presentation;
using InsightFlow.Desktop.Session;

namespace InsightFlow.Desktop.Forms;

internal sealed partial class MainForm : AsyncForm
{
    // Não há push/SSE no backend; a caixa de notificações é consultada periodicamente enquanto a janela está aberta.
    private const int NotificationPollMilliseconds = 120_000;
    private readonly HashSet<Guid> _knownNotifications = [];
    private readonly InsightFlowApiClient _api;
    private readonly UserSession _session;
    private readonly int _sessionVersion;
    private readonly List<WatchlistItemResponse> _watchlist = [];
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _detailCancellation;
    private CancellationTokenSource? _backtestCancellation;
    private int _searchVersion;
    private int _detailVersion;
    private bool _bindingWatchlist;
    private bool _bindingSignals;
    private bool _watchBusy;
    private bool _endingSession;
    private bool _pollingNotifications;
    private bool _notificationsSeeded;
    private bool _dialogOpen;

    public bool LogoutRequested { get; private set; }
    public bool SessionExpired { get; private set; }
    private WatchlistItemResponse? SelectedItem => _watchGrid.CurrentRow?.Tag as WatchlistItemResponse;
    private bool IsCurrentSession => !IsClosing && _session.Version == _sessionVersion;

    public MainForm(InsightFlowApiClient api, UserSession session, Uri apiUrl)
    {
        _api = api;
        _session = session;
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
        _notificationTimer.Tick += (_, _) => PollNotifications();
        _tray.BalloonTipClicked += (_, _) => OpenNotifications();
        _tray.DoubleClick += (_, _) => OpenNotifications();
        Shown += (_, _) =>
        {
            CheckConnection();
            LoadWatchlist();
            _tray.Visible = true;
            PollNotifications();
            _notificationTimer.Start();
        };
        FormClosed += (_, _) =>
        {
            StopNotifications();
            _notificationTimer.Dispose();
            _tray.Dispose();
            _session.Clear();
            _watchlist.Clear();
            _searchResults.Items.Clear();
            _watchGrid.Rows.Clear();
            _history.Rows.Clear();
            _indicators.Rows.Clear();
            _signals.Rows.Clear();
            _backtest.Rows.Clear();
        };
        UpdateActions();
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
        SetStatus(_searchStatus, "Pesquise um ativo para adicioná-lo.");
        UpdateActions();
    }

    private void Search()
    {
        var term = _search.Text.Trim();
        if (!IsCurrentSession || term.Length == 0)
        {
            SetStatus(_searchStatus, "Digite um ticker ou nome para buscar.");
            _search.Focus();
            return;
        }
        InvalidateSearch();
        var version = _searchVersion;
        SetStatus(_searchStatus, "Buscando ativos…");
        Run(async lifetime =>
        {
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            _searchCancellation = request;
            try
            {
                var result = await _api.SearchAssetsAsync(term, request.Token);
                if (!IsCurrentSession || version != _searchVersion || request.IsCancellationRequested) return;
                foreach (var asset in result) _searchResults.Items.Add(new SearchResult(asset));
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
                BindWatchlist(item.Ativo.Id);
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
                var index = _watchGrid.Rows.Add(item.Ativo.Ticker, item.Ativo.Nome);
                _watchGrid.Rows[index].Tag = item;
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

    private void LoadDetails()
    {
        _detailCancellation?.Cancel();
        _backtestCancellation?.Cancel();
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
            var points = await _api.GetHistoryAsync(item.Ativo.Id, period, cancellationToken);
            if (!IsCurrentDetail(version, cancellationToken)) return;
            _history.Rows.Clear();
            _historyChart.SetData(points, item.Ativo.Moeda);
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
                _indicators.Rows.Add(indicator.Tipo, Number(indicator.Valor), indicator.DataCalculo.LocalDateTime,
                    JsonSummary(indicator.Parametros), JsonSummary(indicator.ValoresAuxiliares));
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
                        signal.DataAtivacao.LocalDateTime, signal.RegraDescricao);
                    _signals.Rows[index].Tag = signal;
                }
                if (_signals.Rows.Count > 0) _signals.CurrentCell = _signals.Rows[0].Cells[0];
            }
            finally
            {
                _bindingSignals = false;
            }
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
        _quoteChange.ForeColor = UiTheme.Navy;
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
    }

    private void PollNotifications()
    {
        if (_pollingNotifications || _dialogOpen || !IsCurrentSession) return;
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
                if (IsCurrentSession) EndSession(true);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Falhas da consulta em segundo plano não interrompem o uso; a próxima rodada tenta de novo.
            }
            finally
            {
                _pollingNotifications = false;
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
        SessionExpired = expired;
        LogoutRequested = true;
        StopNotifications();
        _session.Clear();
        _searchCancellation?.Cancel();
        _detailCancellation?.Cancel();
        _backtestCancellation?.Cancel();
        _watchlist.Clear();
        _searchResults.Items.Clear();
        _watchGrid.Rows.Clear();
        _history.Rows.Clear();
        _indicators.Rows.Clear();
        _signals.Rows.Clear();
        _backtest.Rows.Clear();
        Close();
    }

    private static string Number(decimal value) => value.ToString("#,0.########", CultureInfo.CurrentCulture);

    // O backend calcula (preço futuro / preço base − 1) × 100: o valor já está em pontos percentuais.
    private static string NullablePercent(decimal? value) =>
        value is null ? "Indisponível" : value.Value.ToString("+#,0.##;-#,0.##;0", CultureInfo.CurrentCulture) + "%";

    private static string JsonSummary(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return "—";
        var text = value.Value.GetRawText();
        return text.Length <= 120 ? text : text[..117] + "…";
    }

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

    private void OpenPortfolio()
    {
        if (!IsCurrentSession || _dialogOpen) return;
        using var form = new PortfolioForm(_api, _session, _watchlist.Select(item => item.Ativo));
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
        label.ForeColor = error ? UiTheme.Error : UiTheme.Muted;
    }

    private sealed record SearchResult(SearchAssetResponse Asset)
    {
        public string Display => $"{Asset.Ticker} — {Asset.Nome}";
    }
}
