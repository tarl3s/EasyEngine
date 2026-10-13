using System;
using System.Collections.Generic;
using System.Globalization;

namespace EasyEngine
{
    /// <summary>
    /// A tiny, safe expression evaluator for the game mini-language.
    /// Supports numbers, identifiers (resolved by a callback), function calls,
    /// arithmetic (+ - * / %), comparisons (&gt; &lt; &gt;= &lt;= == !=),
    /// logical &amp;&amp; || !, unary minus, and parentheses.
    /// The words a beginner (or an AI) writes are read too: and / or / not, a single = (is), =&gt; =&lt; &lt;&gt;.
    /// Comparisons/logic yield 1.0 (true) or 0.0 (false). Never throws — returns 0 on error.
    /// Identifiers may contain letters, digits, '_', '.', ':' (e.g. self.x, item:key, Player.cx).
    /// </summary>
    public static class Expr
    {
        public static double Eval(string expr, Func<string, double> var, Func<string, double[], double> func)
        {
            if (string.IsNullOrWhiteSpace(expr)) return 0;
            try { return new Parser(expr, var, func).ParseFull(); }
            catch { return 0; }
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;
            private readonly Func<string, double> _var;
            private readonly Func<string, double[], double> _func;

            public Parser(string s, Func<string, double> var, Func<string, double[], double> func)
            { _s = s; _var = var; _func = func; }

            public double ParseFull()
            {
                double v = ParseOr();
                SkipWs();
                return v; // trailing garbage is ignored on purpose (tolerant)
            }

            // precedence: || , && , comparison , + - , * / % , unary , primary
            private double ParseOr()
            {
                double a = ParseAnd();
                while (true)
                {
                    SkipWs();
                    if (Match("||") || MatchWord("or")) { double b = ParseAnd(); a = (a != 0 || b != 0) ? 1 : 0; }
                    else return a;
                }
            }

            private double ParseAnd()
            {
                double a = ParseCompare();
                while (true)
                {
                    SkipWs();
                    if (Match("&&") || MatchWord("and")) { double b = ParseCompare(); a = (a != 0 && b != 0) ? 1 : 0; }
                    else return a;
                }
            }

            private double ParseCompare()
            {
                double a = ParseAdd();
                SkipWs();
                // only one comparison per level (non-associative) is enough here
                if (Match(">=")) return a >= ParseAdd() ? 1 : 0;
                if (Match("<=")) return a <= ParseAdd() ? 1 : 0;
                if (Match("==")) return Math.Abs(a - ParseAdd()) < 1e-6 ? 1 : 0;
                if (Match("!=")) return Math.Abs(a - ParseAdd()) >= 1e-6 ? 1 : 0;
                if (Match("=>")) return a >= ParseAdd() ? 1 : 0;
                if (Match("=<")) return a <= ParseAdd() ? 1 : 0;
                if (Match("<>")) return Math.Abs(a - ParseAdd()) >= 1e-6 ? 1 : 0;
                if (Match(">")) return a > ParseAdd() ? 1 : 0;
                if (Match("<")) return a < ParseAdd() ? 1 : 0;
                if (Match("=")) return Math.Abs(a - ParseAdd()) < 1e-6 ? 1 : 0; // "lives = 0" means "is"
                return a;
            }

            private double ParseAdd()
            {
                double a = ParseMul();
                while (true)
                {
                    SkipWs();
                    if (Match("+")) a += ParseMul();
                    else if (Match("-")) a -= ParseMul();
                    else return a;
                }
            }

            private double ParseMul()
            {
                double a = ParseUnary();
                while (true)
                {
                    SkipWs();
                    if (Match("*")) a *= ParseUnary();
                    else if (Match("/")) { double b = ParseUnary(); a = b == 0 ? 0 : a / b; }
                    else if (Match("%")) { double b = ParseUnary(); a = b == 0 ? 0 : a % b; }
                    else return a;
                }
            }

            private double ParseUnary()
            {
                SkipWs();
                if (Match("-")) return -ParseUnary();
                if (Match("+")) return ParseUnary();
                if (Match("!") || MatchWord("not")) return ParseUnary() == 0 ? 1 : 0;
                return ParsePrimary();
            }

            private double ParsePrimary()
            {
                SkipWs();
                if (Match("("))
                {
                    double v = ParseOr();
                    SkipWs(); Match(")");
                    return v;
                }

                char c = Peek();
                if (char.IsDigit(c) || c == '.')
                    return ParseNumber();

                if (IsIdentStart(c))
                {
                    string name = ParseIdent();
                    SkipWs();
                    if (Peek() == '(') // function call
                    {
                        _i++; // consume '('
                        var args = new List<double>();
                        SkipWs();
                        if (Peek() != ')')
                        {
                            args.Add(ParseOr());
                            SkipWs();
                            while (Match(",")) { args.Add(ParseOr()); SkipWs(); }
                        }
                        SkipWs(); Match(")");
                        return _func(name, args.ToArray());
                    }
                    return _var(name);
                }

                return 0; // unknown char
            }

            private double ParseNumber()
            {
                int start = _i;
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                double.TryParse(_s.Substring(start, _i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var v);
                return v;
            }

            private string ParseIdent()
            {
                int start = _i;
                while (_i < _s.Length && IsIdentChar(_s[_i])) _i++;
                return _s.Substring(start, _i - start);
            }

            private static bool IsIdentStart(char c) => char.IsLetter(c) || c == '_';
            private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == ':';

            private void SkipWs() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }
            private char Peek() { SkipWs(); return _i < _s.Length ? _s[_i] : '\0'; }

            private bool Match(string op)
            {
                SkipWs();
                if (_i + op.Length > _s.Length) return false;
                for (int k = 0; k < op.Length; k++) if (_s[_i + k] != op[k]) return false;
                // for word-less operators this is fine; guard '>' vs '>=' handled by order of checks
                _i += op.Length;
                return true;
            }

            // a word operator (and / or / not, any case) — only a whole word: "android" or "notes" stay names
            private bool MatchWord(string word)
            {
                SkipWs();
                int end = _i + word.Length;
                if (end > _s.Length || string.Compare(_s, _i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
                if (end < _s.Length && IsIdentChar(_s[end])) return false;
                _i = end;
                return true;
            }
        }
    }
}
