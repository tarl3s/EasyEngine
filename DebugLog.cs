using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows.Media;

namespace EasyEngine
{
    public enum LogLevel { Info, Warn, Error }

    /// <summary>One line in the play-mode debug console.</summary>
    public class LogEntry : INotifyPropertyChanged
    {
        private static readonly Brush InfoBrush = Frozen("#D7DAE3"), WarnBrush = Frozen("#F4C04E"), ErrorBrush = Frozen("#FF6B6B"), DimBrush = Frozen("#8E95A8");
        private static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }

        public long Seq { get; init; }
        public double Time { get; set; }
        public long Frame { get; set; }
        public LogLevel Level { get; init; }
        public string Category { get; init; } = "";
        public string Source { get; init; } = "";
        public string Message { get; init; } = "";
        public string Details { get; set; } = "";

        private int _count = 1;
        public int Count { get => _count; set { _count = value; Changed(nameof(Count)); Changed(nameof(CountText)); Changed(nameof(TimeText)); } }

        public string TimeText => $"{Time:0.00}s";
        public string CountText => Count > 1 ? "×" + Count : "";
        public string LevelIcon => Level switch { LogLevel.Warn => "⚠", LogLevel.Error => "⛔", _ => "ⓘ" };
        public Brush LevelBrush => Level switch { LogLevel.Warn => WarnBrush, LogLevel.Error => ErrorBrush, _ => DimBrush };
        public Brush LineBrush => Level switch { LogLevel.Warn => WarnBrush, LogLevel.Error => ErrorBrush, _ => InfoBrush };
        public string Line => $"[{Category}] {(Source.Length > 0 ? Source + ": " : "")}{Message}";

        public string FullText()
        {
            var sb = new StringBuilder();
            sb.Append($"{TimeText}  frame {Frame}  {Level}{(Count > 1 ? $"  ×{Count}" : "")}\n{Line}");
            if (Details.Length > 0) sb.Append("\n\n").Append(Details);
            return sb.ToString();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Changed(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    /// <summary>
    /// In-memory log of what the game is doing (one per PlayWindow). Identical adjacent entries are merged
    /// into one with a ×N count, and it keeps the last <see cref="Capacity"/> entries. UI-free, so the
    /// headless tests can assert on it.
    /// </summary>
    public class DebugLog
    {
        public const int Capacity = 2000;
        private readonly List<LogEntry> _all = new();
        private long _seq;

        public IReadOnlyList<LogEntry> Entries => _all;
        public int Infos { get; private set; }
        public int Warns { get; private set; }
        public int Errors { get; private set; }
        public long LastSeq => _seq;
        public event Action? Cleared;

        public LogEntry Add(double time, long frame, LogLevel level, string category, string source, string message, string details = "")
        {
            switch (level) { case LogLevel.Warn: Warns++; break; case LogLevel.Error: Errors++; break; default: Infos++; break; }
            var last = _all.Count > 0 ? _all[^1] : null;
            if (last != null && last.Level == level && last.Category == category && last.Source == source && last.Message == message)
            {
                last.Time = time; last.Frame = frame;
                if (details.Length > 0) last.Details = details;
                last.Count++;
                return last;
            }
            var e = new LogEntry { Seq = ++_seq, Time = time, Frame = frame, Level = level, Category = category, Source = source, Message = message, Details = details };
            _all.Add(e);
            if (_all.Count > Capacity) _all.RemoveAt(0);
            return e;
        }

        public void Clear()
        {
            _all.Clear(); Infos = Warns = Errors = 0;
            Cleared?.Invoke();
        }

        /// <summary>True if any entry's line or details contains the text (case-insensitive).</summary>
        public bool Has(string text) => _all.Any(e => e.Line.Contains(text, StringComparison.OrdinalIgnoreCase) || e.Details.Contains(text, StringComparison.OrdinalIgnoreCase));

        /// <summary>How many messages (merged counts included) match a category and, optionally, a text.</summary>
        public int CountOf(string category, string? contains = null) =>
            _all.Where(e => (category.Length == 0 || e.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) &&
                            (contains == null || e.Line.Contains(contains, StringComparison.OrdinalIgnoreCase)))
                .Sum(e => e.Count);

        public string ToText() => string.Join(Environment.NewLine, _all.Select(e =>
            $"{e.TimeText,8} #{e.Frame,-6} {e.Level,-5} {e.Line}{(e.Count > 1 ? "  ×" + e.Count : "")}" +
            (e.Details.Length > 0 ? Environment.NewLine + "            " + e.Details.Replace("\n", "\n            ") : "")));
    }
}
