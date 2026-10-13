using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace EasyEngine
{
    public partial class PlayWindow
    {
        /// <summary>Tests only: when set, every key a Custom rule reads is added as "when:key" (event side) or "do:key"
        /// (action side). The parity test (FullTest.Parity.cs) learns from it which keys the game really reads for each
        /// event and action, and checks the block editor has a field for each — nothing to keep in sync by hand.
        /// Null (off) while playing.</summary>
        internal static HashSet<string>? RuleReads;

        /// <summary>A Custom rule's params (one side of "do"); its reads are recorded while <see cref="RuleReads"/> is set.</summary>
        public sealed class RuleBag<TV> : Dictionary<string, TV>
        {
            private readonly string _side;

            public RuleBag(bool trigger) : base(StringComparer.OrdinalIgnoreCase) => _side = trigger ? "when:" : "do:";

            private void Read(string key) => RuleReads?.Add(_side + key.ToLowerInvariant());

            public new bool TryGetValue(string key, [MaybeNullWhen(false)] out TV value) { Read(key); return base.TryGetValue(key, out value); }
            public new bool ContainsKey(string key) { Read(key); return base.ContainsKey(key); }
            public new TV this[string key] { get { Read(key); return base[key]; } set => base[key] = value; }
        }
    }
}
