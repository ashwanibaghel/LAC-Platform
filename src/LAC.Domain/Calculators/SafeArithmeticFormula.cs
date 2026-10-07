namespace LAC.Domain.Calculators;

public sealed record FormulaCalculation(decimal Value, string Normalized, string Substituted);

// A bounded recursive-descent arithmetic parser. No code execution or reflection.
public static class SafeArithmeticFormula
{
    public const int MaxLength = 2048;
    public const int MaxTokens = 256;
    public const int MaxDepth = 32;
    public static FormulaCalculation Evaluate(string? formula, IReadOnlyDictionary<string, decimal> variables)
    {
        if (string.IsNullOrWhiteSpace(formula) || formula.Length > MaxLength)
            throw Error($"Enter an arithmetic formula of at most {MaxLength} characters.");
        try { return new Parser(formula, variables).Parse(); }
        catch (DivideByZeroException) { throw Error("Division by zero is not allowed."); }
        catch (OverflowException) { throw Error("Formula arithmetic overflow."); }
    }
    private static CalculatorValidationException Error(string message) => new("additionalAmount.formula", message);

    private sealed class Parser(string text, IReadOnlyDictionary<string, decimal> variables)
    {
        private int position;
        private int tokens;
        private char Peek { get { while (position < text.Length && char.IsWhiteSpace(text[position])) position++; return position < text.Length ? text[position] : '\0'; } }
        private void Token() { if (++tokens > MaxTokens) throw Error($"Formula exceeds {MaxTokens} tokens."); }
        public FormulaCalculation Parse()
        {
            var result = Expression(0);
            if (Peek != '\0') throw Error($"Unexpected character at position {position + 1}.");
            return result;
        }
        private FormulaCalculation Expression(int depth)
        {
            var left = Product(depth);
            while (Peek is '+' or '-') { var op = Peek; position++; Token(); left = Combine(left, Product(depth), op); }
            return left;
        }
        private FormulaCalculation Product(int depth)
        {
            var left = Atom(depth);
            while (Peek is '*' or '/') { var op = Peek; position++; Token(); left = Combine(left, Atom(depth), op); }
            return left;
        }
        private FormulaCalculation Atom(int depth)
        {
            if (depth > MaxDepth) throw Error($"Formula exceeds {MaxDepth} nesting levels.");
            var current = Peek;
            Token();
            if (current is '+' or '-')
            {
                position++;
                var operand = Atom(depth + 1);
                return new(current == '-' ? -operand.Value : operand.Value, $"({current}{operand.Normalized})", $"({current}{operand.Substituted})");
            }
            if (current == '(')
            {
                position++;
                var value = Expression(depth + 1);
                if (Peek != ')') throw Error("Unbalanced parentheses.");
                position++; Token();
                return value;
            }
            var start = position;
            if (current is >= '0' and <= '9')
            {
                while (position < text.Length && (text[position] is >= '0' and <= '9' || text[position] == '.')) position++;
                if (!CalculatorNumbers.TryParseInput(text[start..position], out var number))
                    throw Error("Invalid number; use plain decimals up to 10^12 with at most 12 fractional digits.");
                var literal = CalculatorNumbers.Text(number);
                return new(number, literal, literal);
            }
            if (current is >= 'A' and <= 'Z' || current == '_')
            {
                while (position < text.Length && (text[position] is >= 'A' and <= 'Z' or >= '0' and <= '9' || text[position] == '_')) position++;
                var name = text[start..position];
                if (!variables.TryGetValue(name, out var value)) throw Error($"Unknown or unavailable variable: {name}.");
                return new(value, name, CalculatorNumbers.Text(value));
            }
            throw Error($"Expected a number, whitelisted variable or parenthesis at position {position + 1}.");
        }
        private static FormulaCalculation Combine(FormulaCalculation a, FormulaCalculation b, char op)
        {
            var value = op switch { '+' => a.Value + b.Value, '-' => a.Value - b.Value,
                '*' => CalculatorNumbers.Multiply(a.Value, b.Value, "additionalAmount.formula"),
                '/' => CalculatorNumbers.Divide(a.Value, b.Value, "additionalAmount.formula"), _ => throw Error("Unsupported operator.") };
            CalculatorNumbers.Result(value, "additionalAmount.formula");
            return new(value, $"({a.Normalized} {op} {b.Normalized})", $"({a.Substituted} {op} {b.Substituted})");
        }
    }
}
