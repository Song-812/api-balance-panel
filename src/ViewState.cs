using System;
using System.ComponentModel;
using System.Globalization;

namespace BalancePanel
{
    // A failed refresh retains the last successful value, but never its fresh status.
    public sealed class RowState : INotifyPropertyChanged
    {
        private string value = "—";
        private string note = "";
        private string status = "未配置";
        private bool busy;
        private bool stale;
        private DateTime? lastSuccess;
        public string Value { get { return value; } }
        public string Note { get { return note; } }
        public string Status { get { return status; } }
        public bool IsBusy { get { return busy; } }
        public bool IsStale { get { return stale; } }
        public bool IsFresh { get { return lastSuccess.HasValue && !stale; } }
        public string Time { get { return lastSuccess.HasValue ? lastSuccess.Value.ToString("HH:mm",CultureInfo.InvariantCulture) : "—"; } }
        public event PropertyChangedEventHandler PropertyChanged;

        public bool BeginRefresh()
        {
            if (busy) return false;
            busy = true;
            status = "正在更新…";
            Notify();
            return true;
        }

        public void Accept(string displayValue, string sourceNote, DateTime timestamp)
        {
            value = displayValue;
            note = sourceNote;
            lastSuccess = timestamp;
            busy = false;
            stale = false;
            status = timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " 更新";
            Notify();
        }

        public void Fail(string message)
        {
            busy = false;
            stale = lastSuccess.HasValue;
            status = message + (lastSuccess.HasValue
                ? " · 上次 " + lastSuccess.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "");
            Notify();
        }

        public void Reset()
        {
            value = "—";
            note = "";
            status = "未配置";
            lastSuccess = null;
            busy = false;
            stale = false;
            Notify();
        }

        private void Notify()
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler == null) return;
            foreach (string name in new [] { "Value", "Note", "Status", "IsBusy", "IsStale", "IsFresh", "Time" })
                handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
