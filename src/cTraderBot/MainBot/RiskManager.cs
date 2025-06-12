public sealed class RiskManager
{
    private static readonly RiskManager _i = new RiskManager();
    public static RiskManager I => _i;

    // ==== CONFIG ====
    public decimal DailyLossLimitPct =  -0.01m;   // -1 %
    public decimal DrawdownLimitPct   =  -0.02m;   // -2 %
    public int     MaxConcurrent      = 3;

    // ==== RUNTIME ====
    private decimal _startEquity;
    private decimal _equityPeak;
    private bool _tradingAllowed;
    private readonly object _lock = new();

    private RiskManager() {}

    public void Init(Account account)
    {
        _startEquity   = account.Balance;
        _equityPeak    = _startEquity;
        _tradingAllowed = true;
    }

    public bool PreTradeGuard(int openPositions)
    {
        lock (_lock)
            return _tradingAllowed && openPositions < MaxConcurrent;
    }

    public void OnPositionUpdate(Account account)
    {
        lock (_lock)
        {
            var realised   = account.Balance - _startEquity;
            var unrealised = account.Equity  - account.Balance;
            var dd         = (account.Equity - _equityPeak) / _equityPeak;
            _equityPeak    = Math.Max(_equityPeak, account.Equity);

            if (realised + unrealised <= _startEquity * DailyLossLimitPct ||
                dd <= DrawdownLimitPct)
                TriggerShutdown("Risk limit hit");
        }
    }

    private void TriggerShutdown(string reason)
    {
        _tradingAllowed = false;
        // fire event -> bot closes positions & cancels orders
        OnRiskBreach?.Invoke(this, reason);
        PersistState();
    }

    public event EventHandler<string> OnRiskBreach;
    public bool IsTradingAllowed => _tradingAllowed;

    public void ResetForNewDay(Account account)
    {
        lock (_lock)
        {
            _startEquity   = account.Balance;
            _equityPeak    = _startEquity;
            _tradingAllowed = true;
        }
    }

    private void PersistState() => File.WriteAllText("RiskState.json", JsonSerializer.Serialize(this));
}