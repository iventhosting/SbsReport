using System.Collections.Concurrent;
using System.Globalization;
using SbsReport.Models;

namespace SbsReport.Services;

public sealed class EvalContext
{
    public SbsReportDocument Report { get; init; } = null!;
    public Dictionary<string, object?>? Row { get; init; }
    public List<Dictionary<string, object?>>? AllRows { get; init; }
    public int LineNumber { get; init; }
    public int GroupLine { get; init; }
    public int LineThrough { get; init; }
    public int PageNumber { get; init; }
    public int TotalPageCount { get; init; }
}

/// <summary>
/// Hızlı, önbellekli (compiled AST) ifade motoru.
/// CodeDOM derleme gecikmesi olmadan satır başına mikrosaniye hızında çalışır.
/// </summary>
public static class ExpressionEvaluator
{
    private static readonly CultureInfo TrCulture = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly ConcurrentDictionary<string, List<TemplateSegment>> TemplateCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ExprNode> ExprCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> NormKeyCache = new(StringComparer.Ordinal);

    private readonly record struct TemplateSegment(bool IsExpr, string Literal, ExprNode? Node);

    public static string EvaluateTemplate(string? template, string? format, EvalContext ctx)
    {
        if (string.IsNullOrEmpty(template))
            return string.Empty;

        if (!template.Contains('{'))
            return template;

        var segments = TemplateCache.GetOrAdd(template, CompileTemplate);
        if (segments.Count == 1 && segments[0].IsExpr)
        {
            var val = EvaluateNode(segments[0].Node!, ctx);
            return FormatValue(val, format);
        }

        var sb = new System.Text.StringBuilder(template.Length + 16);
        foreach (var seg in segments)
        {
            if (!seg.IsExpr)
            {
                sb.Append(seg.Literal);
            }
            else
            {
                var val = EvaluateNode(seg.Node!, ctx);
                sb.Append(FormatValue(val, format));
            }
        }
        return sb.ToString();
    }

    public static bool EvaluateCondition(string? expression, EvalContext ctx)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        var clean = expression.Trim();
        if (clean.StartsWith('{') && clean.EndsWith('}'))
            clean = clean[1..^1].Trim();

        var node = ExprCache.GetOrAdd(clean, ParseExpression);
        return ToBool(EvaluateNode(node, ctx));
    }

    public static object? EvaluateExpression(string expression, EvalContext ctx)
    {
        var node = ExprCache.GetOrAdd(expression.Trim(), ParseExpression);
        return EvaluateNode(node, ctx);
    }

    private static List<TemplateSegment> CompileTemplate(string template)
    {
        var list = new List<TemplateSegment>();
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf('{', i);
            if (open < 0)
            {
                list.Add(new TemplateSegment(false, template[i..], null));
                break;
            }
            if (open > i)
                list.Add(new TemplateSegment(false, template[i..open], null));

            int close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                list.Add(new TemplateSegment(false, template[open..], null));
                break;
            }

            var exprStr = template[(open + 1)..close].Trim();
            var node = ExprCache.GetOrAdd(exprStr, ParseExpression);
            list.Add(new TemplateSegment(true, string.Empty, node));
            i = close + 1;
        }
        return list;
    }

    // ---------- AST Nodes ----------
    private abstract record ExprNode;
    private sealed record LiteralNode(object? Value) : ExprNode;
    private sealed record IdentNode(string Name) : ExprNode;
    private sealed record UnaryNode(string Op, ExprNode Operand) : ExprNode;
    private sealed record BinaryNode(string Op, ExprNode Left, ExprNode Right) : ExprNode;
    private sealed record CallNode(string FuncName, List<ExprNode> Args) : ExprNode;

    // ---------- Lexer & Recursive-Descent Parser ----------
    private enum TokKind { Eof, Number, String, Ident, Op, LParen, RParen, Comma }
    private readonly record struct Token(TokKind Kind, string Text);

    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '"' || c == '\'')
            {
                char q = c;
                i++;
                int start = i;
                while (i < s.Length && s[i] != q) i++;
                tokens.Add(new Token(TokKind.String, s[start..i]));
                if (i < s.Length) i++;
                continue;
            }

            if (char.IsDigit(c))
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
                tokens.Add(new Token(TokKind.Number, s[start..i]));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || c == '_' || s[i] == '_' || s[i] == '.')) i++;
                tokens.Add(new Token(TokKind.Ident, s[start..i]));
                continue;
            }

            if (c == '(') { tokens.Add(new Token(TokKind.LParen, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokKind.RParen, ")")); i++; continue; }
            if (c == ',') { tokens.Add(new Token(TokKind.Comma, ",")); i++; continue; }

            // Two-char operators
            if (i + 1 < s.Length)
            {
                var two = s.Substring(i, 2);
                if (two is "==" or "!=" or "<=" or ">=" or "&&" or "||")
                {
                    tokens.Add(new Token(TokKind.Op, two));
                    i += 2;
                    continue;
                }
            }

            tokens.Add(new Token(TokKind.Op, c.ToString()));
            i++;
        }
        tokens.Add(new Token(TokKind.Eof, ""));
        return tokens;
    }

    private static ExprNode ParseExpression(string text)
    {
        try
        {
            var p = new Parser(Tokenize(text));
            return p.ParseOr();
        }
        catch
        {
            return new IdentNode(text);
        }
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private int _pos;
        public Parser(List<Token> tokens) => _tokens = tokens;
        private Token Peek => _tokens[_pos];
        private Token Next() => _tokens[_pos++];
        private bool Match(TokKind kind, string? text = null)
        {
            if (Peek.Kind == kind && (text == null || Peek.Text == text))
            {
                _pos++;
                return true;
            }
            return false;
        }

        public ExprNode ParseOr()
        {
            var left = ParseAnd();
            while (Match(TokKind.Op, "||")) left = new BinaryNode("||", left, ParseAnd());
            return left;
        }

        private ExprNode ParseAnd()
        {
            var left = ParseEquality();
            while (Match(TokKind.Op, "&&")) left = new BinaryNode("&&", left, ParseEquality());
            return left;
        }

        private ExprNode ParseEquality()
        {
            var left = ParseComparison();
            while (Peek.Kind == TokKind.Op && (Peek.Text == "==" || Peek.Text == "!=" || Peek.Text == "="))
            {
                var op = Next().Text;
                left = new BinaryNode(op == "=" ? "==" : op, left, ParseComparison());
            }
            return left;
        }

        private ExprNode ParseComparison()
        {
            var left = ParseAddSub();
            while (Peek.Kind == TokKind.Op && (Peek.Text is "<" or "<=" or ">" or ">="))
            {
                var op = Next().Text;
                left = new BinaryNode(op, left, ParseAddSub());
            }
            return left;
        }

        private ExprNode ParseAddSub()
        {
            var left = ParseMulDiv();
            while (Peek.Kind == TokKind.Op && (Peek.Text is "+" or "-"))
            {
                var op = Next().Text;
                left = new BinaryNode(op, left, ParseMulDiv());
            }
            return left;
        }

        private ExprNode ParseMulDiv()
        {
            var left = ParseUnary();
            while (Peek.Kind == TokKind.Op && (Peek.Text is "*" or "/" or "%"))
            {
                var op = Next().Text;
                left = new BinaryNode(op, left, ParseUnary());
            }
            return left;
        }

        private ExprNode ParseUnary()
        {
            if (Match(TokKind.Op, "-")) return new UnaryNode("-", ParseUnary());
            if (Match(TokKind.Op, "!")) return new UnaryNode("!", ParseUnary());
            return ParsePrimary();
        }

        private ExprNode ParsePrimary()
        {
            var t = Peek;
            if (t.Kind == TokKind.Number)
            {
                Next();
                return new LiteralNode(decimal.Parse(t.Text, CultureInfo.InvariantCulture));
            }
            if (t.Kind == TokKind.String)
            {
                Next();
                return new LiteralNode(t.Text);
            }
            if (t.Kind == TokKind.Ident)
            {
                Next();
                if (t.Text.Equals("true", StringComparison.OrdinalIgnoreCase)) return new LiteralNode(true);
                if (t.Text.Equals("false", StringComparison.OrdinalIgnoreCase)) return new LiteralNode(false);
                if (t.Text.Equals("null", StringComparison.OrdinalIgnoreCase)) return new LiteralNode(null);

                if (Match(TokKind.LParen))
                {
                    var args = new List<ExprNode>();
                    if (!Match(TokKind.RParen))
                    {
                        do { args.Add(ParseOr()); } while (Match(TokKind.Comma));
                        Match(TokKind.RParen);
                    }
                    return new CallNode(t.Text, args);
                }
                return new IdentNode(t.Text);
            }
            if (Match(TokKind.LParen))
            {
                var inner = ParseOr();
                Match(TokKind.RParen);
                return inner;
            }
            Next();
            return new LiteralNode(null);
        }
    }

    // ---------- Evaluator ----------
    private static object? EvaluateNode(ExprNode node, EvalContext ctx) => node switch
    {
        LiteralNode lit => lit.Value,
        IdentNode id => ResolveIdentifier(id.Name, ctx),
        UnaryNode u => u.Op == "-" ? -ToDecimal(EvaluateNode(u.Operand, ctx)) : !ToBool(EvaluateNode(u.Operand, ctx)),
        BinaryNode b => EvaluateBinary(b, ctx),
        CallNode c => EvaluateCall(c, ctx),
        _ => null
    };

    /// <summary>
    /// Türkçe karakter ve büyük/küçük harf duyarsız anahtar normalizasyonu.
    /// Örn: "Ünvan" == "UNVAN" == "unvan", "İsim" == "ISIM" == "isim", "Şehir" == "SEHIR"
    /// </summary>
    public static string NormalizeTurkishKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        return NormKeyCache.GetOrAdd(key, static k =>
        {
            Span<char> buf = k.Length <= 128 ? stackalloc char[k.Length] : new char[k.Length];
            for (int i = 0; i < k.Length; i++)
            {
                buf[i] = k[i] switch
                {
                    'İ' or 'I' or 'ı' or 'i' => 'i',
                    'Ş' or 'ş' => 's',
                    'Ğ' or 'ğ' => 'g',
                    'Ü' or 'ü' => 'u',
                    'Ö' or 'ö' => 'o',
                    'Ç' or 'ç' => 'c',
                    _ => char.ToLowerInvariant(k[i])
                };
            }
            return new string(buf);
        });
    }

    public static bool TryGetRowValue(Dictionary<string, object?>? row, string colName, out object? value)
    {
        value = null;
        if (row == null || string.IsNullOrEmpty(colName)) return false;
        if (row.TryGetValue(colName, out value)) return true;

        var normTarget = NormalizeTurkishKey(colName);
        foreach (var kv in row)
        {
            if (NormalizeTurkishKey(kv.Key) == normTarget)
            {
                value = kv.Value;
                return true;
            }
        }
        return false;
    }

    private static object? ResolveIdentifier(string name, EvalContext ctx)
    {
        if (name.Equals("PageNumber", StringComparison.OrdinalIgnoreCase)) return ctx.PageNumber;
        if (name.Equals("TotalPageCount", StringComparison.OrdinalIgnoreCase))
            return ctx.TotalPageCount > 0 ? ctx.TotalPageCount : "__TOTAL_PAGES__";
        if (name.Equals("PageNofM", StringComparison.OrdinalIgnoreCase))
            return $"{ctx.PageNumber} / {(ctx.TotalPageCount > 0 ? ctx.TotalPageCount.ToString(CultureInfo.InvariantCulture) : "__TOTAL_PAGES__")}";
        if (name.Equals("IsFirstPage", StringComparison.OrdinalIgnoreCase)) return ctx.PageNumber <= 1;
        if (name.Equals("IsLastPage", StringComparison.OrdinalIgnoreCase))
            return ctx.TotalPageCount > 0 && ctx.PageNumber == ctx.TotalPageCount;
        if (name.Equals("Line", StringComparison.OrdinalIgnoreCase)) return ctx.LineNumber;
        if (name.Equals("GroupLine", StringComparison.OrdinalIgnoreCase)) return ctx.GroupLine > 0 ? ctx.GroupLine : ctx.LineNumber;
        if (name.Equals("LineThrough", StringComparison.OrdinalIgnoreCase)) return ctx.LineThrough > 0 ? ctx.LineThrough : ctx.LineNumber;
        if (name.Equals("Today", StringComparison.OrdinalIgnoreCase)) return DateTime.Now.ToString("dd.MM.yyyy", TrCulture);
        if (name.Equals("Time", StringComparison.OrdinalIgnoreCase)) return DateTime.Now.ToString("HH:mm:ss", TrCulture);
        if (name.Equals("Now", StringComparison.OrdinalIgnoreCase)) return DateTime.Now.ToString("dd.MM.yyyy HH:mm", TrCulture);
        if (name.Equals("ReportName", StringComparison.OrdinalIgnoreCase)) return ctx.Report.Name;

        if (ctx.Report.Variables.TryGetValue(name, out var varVal))
            return varVal;
        // Turkish-safe & case-insensitive variable lookup
        var normName = NormalizeTurkishKey(name);
        foreach (var kv in ctx.Report.Variables)
        {
            if (NormalizeTurkishKey(kv.Key) == normName)
                return kv.Value;
        }

        var colName = name.Contains('.') ? name.Split('.').Last() : name;
        if (TryGetRowValue(ctx.Row, colName, out var rowVal))
            return rowVal;

        return null;
    }

    private static object? EvaluateBinary(BinaryNode b, EvalContext ctx)
    {
        if (b.Op == "&&") return ToBool(EvaluateNode(b.Left, ctx)) && ToBool(EvaluateNode(b.Right, ctx));
        if (b.Op == "||") return ToBool(EvaluateNode(b.Left, ctx)) || ToBool(EvaluateNode(b.Right, ctx));

        var l = EvaluateNode(b.Left, ctx);
        var r = EvaluateNode(b.Right, ctx);

        if (b.Op == "+")
        {
            if (l is string || r is string)
                return $"{l}{r}";
            return ToDecimal(l) + ToDecimal(r);
        }
        if (b.Op == "-") return ToDecimal(l) - ToDecimal(r);
        if (b.Op == "*") return ToDecimal(l) * ToDecimal(r);
        if (b.Op == "/") { var rv = ToDecimal(r); return rv == 0 ? 0m : ToDecimal(l) / rv; }
        if (b.Op == "%") { var rv = ToDecimal(r); return rv == 0 ? 0m : ToDecimal(l) % rv; }

        if (b.Op == "==") return CompareValues(l, r) == 0;
        if (b.Op == "!=") return CompareValues(l, r) != 0;
        if (b.Op == "<") return CompareValues(l, r) < 0;
        if (b.Op == "<=") return CompareValues(l, r) <= 0;
        if (b.Op == ">") return CompareValues(l, r) > 0;
        if (b.Op == ">=") return CompareValues(l, r) >= 0;

        return null;
    }

    private static object? EvaluateCall(CallNode c, EvalContext ctx)
    {
        var fn = c.FuncName.ToLowerInvariant();
        var rows = ctx.AllRows ?? new();

        switch (fn)
        {
            case "count":
                return rows.Count;
            case "runningcount":
                return ctx.LineNumber;
            case "countdistinct":
            {
                if (c.Args.Count == 0 || rows.Count == 0) return 0;
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < rows.Count; i++)
                {
                    var v = EvaluateNode(c.Args[0], RowCtx(ctx, rows, i));
                    if (v != null && !string.IsNullOrEmpty(v.ToString())) set.Add(v.ToString()!);
                }
                return set.Count;
            }
            case "first":
                return (c.Args.Count > 0 && rows.Count > 0) ? EvaluateNode(c.Args[0], RowCtx(ctx, rows, 0)) : null;
            case "last":
                return (c.Args.Count > 0 && rows.Count > 0) ? EvaluateNode(c.Args[0], RowCtx(ctx, rows, rows.Count - 1)) : null;
            case "runningsum":
            {
                if (c.Args.Count == 0 || rows.Count == 0) return 0m;
                int limit = ctx.LineNumber > 0 ? Math.Min(ctx.LineNumber, rows.Count) : rows.Count;
                decimal acc = 0m;
                for (int i = 0; i < limit; i++)
                    acc += ToDecimal(EvaluateNode(c.Args[0], RowCtx(ctx, rows, i)));
                return acc;
            }
            case "sumdistinct":
            {
                if (c.Args.Count == 0 || rows.Count == 0) return 0m;
                var seen = new HashSet<decimal>();
                decimal acc = 0m;
                for (int i = 0; i < rows.Count; i++)
                {
                    var val = ToDecimal(EvaluateNode(c.Args[0], RowCtx(ctx, rows, i)));
                    if (seen.Add(val)) acc += val;
                }
                return acc;
            }
            case "median":
            {
                if (c.Args.Count == 0 || rows.Count == 0) return 0m;
                var vals = new List<decimal>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                    vals.Add(ToDecimal(EvaluateNode(c.Args[0], RowCtx(ctx, rows, i))));
                vals.Sort();
                int mid = vals.Count / 2;
                return vals.Count % 2 == 0 ? (vals[mid - 1] + vals[mid]) / 2m : vals[mid];
            }
            case "sum":
            case "avg":
            case "min":
            case "max":
            {
                if (c.Args.Count == 0 || rows.Count == 0) return 0m;
                var argNode = c.Args[0];
                var vals = new List<decimal>(rows.Count);
                for (int i = 0; i < rows.Count; i++)
                    vals.Add(ToDecimal(EvaluateNode(argNode, RowCtx(ctx, rows, i))));
                return fn switch
                {
                    "sum" => vals.Sum(),
                    "avg" => vals.Average(),
                    "min" => vals.Min(),
                    "max" => vals.Max(),
                    _ => 0m
                };
            }
            case "iif":
                if (c.Args.Count < 3) return null;
                return ToBool(EvaluateNode(c.Args[0], ctx))
                    ? EvaluateNode(c.Args[1], ctx)
                    : EvaluateNode(c.Args[2], ctx);
            case "switch":
            {
                for (int i = 0; i + 1 < c.Args.Count; i += 2)
                {
                    if (ToBool(EvaluateNode(c.Args[i], ctx)))
                        return EvaluateNode(c.Args[i + 1], ctx);
                }
                return c.Args.Count % 2 == 1 ? EvaluateNode(c.Args[^1], ctx) : null;
            }
            case "choose":
            {
                if (c.Args.Count < 2) return null;
                int idx = (int)ToDecimal(EvaluateNode(c.Args[0], ctx));
                return (idx >= 1 && idx < c.Args.Count) ? EvaluateNode(c.Args[idx], ctx) : null;
            }
            case "isnull":
                return c.Args.Count == 0 || EvaluateNode(c.Args[0], ctx) is null;
            case "isempty":
            {
                var v = c.Args.Count > 0 ? EvaluateNode(c.Args[0], ctx) : null;
                return v is null || string.IsNullOrWhiteSpace(v.ToString());
            }
            case "round":
            {
                if (c.Args.Count == 0) return 0m;
                var v = ToDecimal(EvaluateNode(c.Args[0], ctx));
                var d = c.Args.Count > 1 ? (int)ToDecimal(EvaluateNode(c.Args[1], ctx)) : 2;
                return Math.Round(v, Math.Clamp(d, 0, 15));
            }
            case "abs":
                return c.Args.Count > 0 ? Math.Abs(ToDecimal(EvaluateNode(c.Args[0], ctx))) : 0m;
            case "ceiling":
                return c.Args.Count > 0 ? Math.Ceiling(ToDecimal(EvaluateNode(c.Args[0], ctx))) : 0m;
            case "floor":
                return c.Args.Count > 0 ? Math.Floor(ToDecimal(EvaluateNode(c.Args[0], ctx))) : 0m;
            case "truncate":
                return c.Args.Count > 0 ? Math.Truncate(ToDecimal(EvaluateNode(c.Args[0], ctx))) : 0m;
            case "pow":
                return c.Args.Count >= 2 ? (decimal)Math.Pow((double)ToDecimal(EvaluateNode(c.Args[0], ctx)), (double)ToDecimal(EvaluateNode(c.Args[1], ctx))) : 0m;
            case "sqrt":
            {
                var v = c.Args.Count > 0 ? (double)ToDecimal(EvaluateNode(c.Args[0], ctx)) : 0;
                return v < 0 ? 0m : (decimal)Math.Sqrt(v);
            }
            case "minval":
                return c.Args.Count >= 2 ? Math.Min(ToDecimal(EvaluateNode(c.Args[0], ctx)), ToDecimal(EvaluateNode(c.Args[1], ctx))) : 0m;
            case "maxval":
                return c.Args.Count >= 2 ? Math.Max(ToDecimal(EvaluateNode(c.Args[0], ctx)), ToDecimal(EvaluateNode(c.Args[1], ctx))) : 0m;
            case "upper":
            case "touppercase":
                return c.Args.Count > 0 ? Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture)?.ToUpper(TrCulture) : "";
            case "lower":
            case "tolowercase":
                return c.Args.Count > 0 ? Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture)?.ToLower(TrCulture) : "";
            case "propercase":
            case "topropercase":
            {
                var s = c.Args.Count > 0 ? Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "" : "";
                return TrCulture.TextInfo.ToTitleCase(s.ToLower(TrCulture));
            }
            case "trim":
                return c.Args.Count > 0 ? (Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "").Trim() : "";
            case "trimstart":
                return c.Args.Count > 0 ? (Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "").TrimStart() : "";
            case "trimend":
                return c.Args.Count > 0 ? (Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "").TrimEnd() : "";
            case "length":
            case "len":
                return c.Args.Count > 0 ? (Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "").Length : 0;
            case "left":
            {
                if (c.Args.Count < 2) return "";
                var s = Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "";
                int n = Math.Max(0, (int)ToDecimal(EvaluateNode(c.Args[1], ctx)));
                return n >= s.Length ? s : s[..n];
            }
            case "right":
            {
                if (c.Args.Count < 2) return "";
                var s = Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "";
                int n = Math.Max(0, (int)ToDecimal(EvaluateNode(c.Args[1], ctx)));
                return n >= s.Length ? s : s[^n..];
            }
            case "substring":
            case "mid":
            {
                if (c.Args.Count < 2) return "";
                var s = Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "";
                int start = Math.Max(0, (int)ToDecimal(EvaluateNode(c.Args[1], ctx)));
                if (start >= s.Length) return "";
                if (c.Args.Count >= 3)
                {
                    int len = Math.Max(0, (int)ToDecimal(EvaluateNode(c.Args[2], ctx)));
                    return s.Substring(start, Math.Min(len, s.Length - start));
                }
                return s[start..];
            }
            case "replace":
            {
                if (c.Args.Count < 3) return "";
                var s = Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "";
                var oldV = Convert.ToString(EvaluateNode(c.Args[1], ctx), TrCulture) ?? "";
                var newV = Convert.ToString(EvaluateNode(c.Args[2], ctx), TrCulture) ?? "";
                return string.IsNullOrEmpty(oldV) ? s : s.Replace(oldV, newV, StringComparison.OrdinalIgnoreCase);
            }
            case "contains":
            {
                if (c.Args.Count < 2) return false;
                var s = Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "";
                var sub = Convert.ToString(EvaluateNode(c.Args[1], ctx), TrCulture) ?? "";
                return s.Contains(sub, StringComparison.OrdinalIgnoreCase);
            }
            case "startswith":
            {
                if (c.Args.Count < 2) return false;
                var s = Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "";
                var sub = Convert.ToString(EvaluateNode(c.Args[1], ctx), TrCulture) ?? "";
                return s.StartsWith(sub, StringComparison.OrdinalIgnoreCase);
            }
            case "endswith":
            {
                if (c.Args.Count < 2) return false;
                var s = Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "";
                var sub = Convert.ToString(EvaluateNode(c.Args[1], ctx), TrCulture) ?? "";
                return s.EndsWith(sub, StringComparison.OrdinalIgnoreCase);
            }
            case "padleft":
            {
                if (c.Args.Count < 2) return "";
                var s = Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "";
                int w = (int)ToDecimal(EvaluateNode(c.Args[1], ctx));
                char ch = c.Args.Count >= 3 && (Convert.ToString(EvaluateNode(c.Args[2], ctx), TrCulture) ?? " ").Length > 0
                    ? (Convert.ToString(EvaluateNode(c.Args[2], ctx), TrCulture) ?? " ")[0]
                    : '0';
                return s.PadLeft(w, ch);
            }
            case "padright":
            {
                if (c.Args.Count < 2) return "";
                var s = Convert.ToString(EvaluateNode(c.Args[0], ctx), TrCulture) ?? "";
                int w = (int)ToDecimal(EvaluateNode(c.Args[1], ctx));
                char ch = c.Args.Count >= 3 && (Convert.ToString(EvaluateNode(c.Args[2], ctx), TrCulture) ?? " ").Length > 0
                    ? (Convert.ToString(EvaluateNode(c.Args[2], ctx), TrCulture) ?? " ")[0]
                    : ' ';
                return s.PadRight(w, ch);
            }
            case "isnumeric":
            {
                var v = c.Args.Count > 0 ? EvaluateNode(c.Args[0], ctx) : null;
                if (v is null) return false;
                if (IsNumeric(v)) return true;
                var s = v.ToString();
                return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                       decimal.TryParse(s, NumberStyles.Any, TrCulture, out _);
            }
            case "towordstr":
            case "towords":
                return c.Args.Count > 0 ? NumberToWordsTr((long)Math.Truncate(ToDecimal(EvaluateNode(c.Args[0], ctx)))) : "";
            case "tocurrencywordstr":
            case "tocurrencywords":
            {
                if (c.Args.Count == 0) return "";
                var amt = ToDecimal(EvaluateNode(c.Args[0], ctx));
                var mainUnit = c.Args.Count > 1 ? Convert.ToString(EvaluateNode(c.Args[1], ctx), TrCulture) ?? "Türk Lirası" : "Türk Lirası";
                var subUnit = c.Args.Count > 2 ? Convert.ToString(EvaluateNode(c.Args[2], ctx), TrCulture) ?? "Kuruş" : "Kuruş";
                return CurrencyToWordsTr(amt, mainUnit, subUnit);
            }
            case "year":
                return TryToDate(c.Args.Count > 0 ? EvaluateNode(c.Args[0], ctx) : null, out var dy) ? dy.Year : 0;
            case "month":
                return TryToDate(c.Args.Count > 0 ? EvaluateNode(c.Args[0], ctx) : null, out var dm) ? dm.Month : 0;
            case "day":
                return TryToDate(c.Args.Count > 0 ? EvaluateNode(c.Args[0], ctx) : null, out var dd) ? dd.Day : 0;
            case "hour":
                return TryToDate(c.Args.Count > 0 ? EvaluateNode(c.Args[0], ctx) : null, out var dh) ? dh.Hour : 0;
            case "minute":
                return TryToDate(c.Args.Count > 0 ? EvaluateNode(c.Args[0], ctx) : null, out var dmi) ? dmi.Minute : 0;
            case "monthname":
            {
                if (c.Args.Count == 0) return "";
                var v = EvaluateNode(c.Args[0], ctx);
                int m = TryToDate(v, out var dt) ? dt.Month : (int)ToDecimal(v);
                return (m >= 1 && m <= 12) ? TrCulture.DateTimeFormat.GetMonthName(m) : "";
            }
            case "daysinmonth":
            {
                if (c.Args.Count >= 2)
                {
                    int y = (int)ToDecimal(EvaluateNode(c.Args[0], ctx));
                    int m = (int)ToDecimal(EvaluateNode(c.Args[1], ctx));
                    return (y >= 1 && m >= 1 && m <= 12) ? DateTime.DaysInMonth(y, m) : 0;
                }
                return TryToDate(c.Args.Count > 0 ? EvaluateNode(c.Args[0], ctx) : null, out var dt) ? DateTime.DaysInMonth(dt.Year, dt.Month) : 0;
            }
            case "adddays":
            {
                if (c.Args.Count < 2) return "";
                if (!TryToDate(EvaluateNode(c.Args[0], ctx), out var dt)) return "";
                int days = (int)ToDecimal(EvaluateNode(c.Args[1], ctx));
                return dt.AddDays(days).ToString("dd.MM.yyyy", TrCulture);
            }
            case "datediff":
            {
                if (c.Args.Count < 2) return 0;
                if (!TryToDate(EvaluateNode(c.Args[0], ctx), out var d1) || !TryToDate(EvaluateNode(c.Args[1], ctx), out var d2))
                    return 0;
                return (int)(d1.Date - d2.Date).TotalDays;
            }
            case "coalesce":
                foreach (var a in c.Args)
                {
                    var val = EvaluateNode(a, ctx);
                    if (val != null && !string.IsNullOrEmpty(val.ToString())) return val;
                }
                return "";
            case "format":
            {
                if (c.Args.Count < 2) return "";
                var val = EvaluateNode(c.Args[0], ctx);
                var fmt = Convert.ToString(EvaluateNode(c.Args[1], ctx), TrCulture);
                return FormatValue(val, fmt);
            }
            default:
                return null;
        }
    }

    private static EvalContext RowCtx(EvalContext ctx, List<Dictionary<string, object?>> rows, int index) => new()
    {
        Report = ctx.Report,
        Row = rows[index],
        AllRows = rows,
        LineNumber = index + 1,
        GroupLine = ctx.GroupLine,
        LineThrough = ctx.LineThrough,
        PageNumber = ctx.PageNumber,
        TotalPageCount = ctx.TotalPageCount
    };

    private static bool TryToDate(object? v, out DateTime dt)
    {
        if (v is DateTime d) { dt = d; return true; }
        if (v is DateTimeOffset dto) { dt = dto.DateTime; return true; }
        if (v is DateOnly dOnly) { dt = dOnly.ToDateTime(TimeOnly.MinValue); return true; }
        var s = v?.ToString();
        if (!string.IsNullOrWhiteSpace(s) &&
            (DateTime.TryParse(s, TrCulture, DateTimeStyles.None, out dt) ||
             DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)))
            return true;
        dt = default;
        return false;
    }

    private static readonly string[] TrOnes = ["", "Bir", "İki", "Üç", "Dört", "Beş", "Altı", "Yedi", "Sekiz", "Dokuz"];
    private static readonly string[] TrTens = ["", "On", "Yirmi", "Otuz", "Kırk", "Elli", "Altmış", "Yetmiş", "Seksen", "Doksan"];
    private static readonly string[] TrScales = ["", "Bin", "Milyon", "Milyar", "Trilyon"];

    public static string NumberToWordsTr(long number)
    {
        if (number == 0) return "Sıfır";
        var negative = number < 0;
        ulong n = negative ? (ulong)(-number) : (ulong)number;
        var parts = new List<string>();
        int scale = 0;
        while (n > 0 && scale < TrScales.Length)
        {
            int chunk = (int)(n % 1000);
            if (chunk > 0)
            {
                var chunkWords = new List<string>();
                int h = chunk / 100;
                int t = (chunk / 10) % 10;
                int o = chunk % 10;
                if (h == 1) chunkWords.Add("Yüz");
                else if (h > 1) { chunkWords.Add(TrOnes[h]); chunkWords.Add("Yüz"); }
                if (t > 0) chunkWords.Add(TrTens[t]);
                if (o > 0 && !(scale == 1 && chunk == 1)) chunkWords.Add(TrOnes[o]);
                if (scale > 0) chunkWords.Add(TrScales[scale]);
                parts.Insert(0, string.Join(" ", chunkWords));
            }
            n /= 1000;
            scale++;
        }
        var res = string.Join(" ", parts);
        return negative ? "Eksi " + res : res;
    }

    public static string CurrencyToWordsTr(decimal amount, string mainUnit = "Türk Lirası", string subUnit = "Kuruş")
    {
        var rounded = Math.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        long whole = (long)Math.Truncate(rounded);
        int frac = (int)((rounded - whole) * 100m);
        var sb = new System.Text.StringBuilder("Yalnız ");
        if (amount < 0) sb.Append("Eksi ");
        sb.Append(NumberToWordsTr(whole)).Append(' ').Append(mainUnit);
        if (frac > 0)
            sb.Append(' ').Append(NumberToWordsTr(frac)).Append(' ').Append(subUnit);
        return sb.ToString();
    }

    private static int CompareValues(object? l, object? r)
    {
        if (l is null && r is null) return 0;
        if (l is null) return -1;
        if (r is null) return 1;
        if (IsNumeric(l) && IsNumeric(r))
            return ToDecimal(l).CompareTo(ToDecimal(r));
        return string.Compare(l.ToString(), r.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNumeric(object v) =>
        v is int or long or decimal or double or float or short or byte;

    public static decimal ToDecimal(object? v)
    {
        if (v is null) return 0m;
        return v switch
        {
            decimal d => d,
            int i => i,
            long l => l,
            double db => (decimal)db,
            float f => (decimal)f,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) => p,
            string s when decimal.TryParse(s, NumberStyles.Any, TrCulture, out var p2) => p2,
            _ => 0m
        };
    }

    private static bool ToBool(object? v) => v switch
    {
        null => false,
        bool b => b,
        decimal d => d != 0,
        int i => i != 0,
        string s => !string.IsNullOrWhiteSpace(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase) && s != "0",
        _ => true
    };

    public static string FormatValue(object? val, string? format)
    {
        if (val is null) return string.Empty;
        if (!string.IsNullOrWhiteSpace(format) && val is IFormattable formattable)
        {
            try { return formattable.ToString(format, TrCulture); } catch { }
        }
        if (val is decimal d) return d.ToString("N2", TrCulture);
        if (val is double db) return db.ToString("N2", TrCulture);
        return Convert.ToString(val, TrCulture) ?? string.Empty;
    }
}
