export function evaluateExpression(source) {
  const tokens = String(source || "").match(/\d*\.?\d+|[()+\-*/%]/g) || [];
  if (!tokens.length || tokens.join("") !== String(source).replace(/\s+/g, "")) throw new Error("Invalid expression.");
  let index = 0;
  const primary = () => {
    const token = tokens[index++];
    if (token === "(") { const value = expression(); if (tokens[index++] !== ")") throw new Error("Missing closing bracket."); return value; }
    if (token === "-") return -primary();
    const value = Number(token);
    if (!Number.isFinite(value)) throw new Error("Expected a number.");
    return value;
  };
  const factor = () => { let value = primary(); while (tokens[index] === "%") { index++; value /= 100; } return value; };
  const term = () => { let value = factor(); while (["*", "/"].includes(tokens[index])) { const op = tokens[index++]; const right = factor(); if (op === "/" && right === 0) throw new Error("Cannot divide by zero."); value = op === "*" ? value * right : value / right; } return value; };
  const expression = () => { let value = term(); while (["+", "-"].includes(tokens[index])) { const op = tokens[index++]; const right = term(); value = op === "+" ? value + right : value - right; } return value; };
  const result = expression();
  if (index !== tokens.length || !Number.isFinite(result)) throw new Error("Invalid expression.");
  return result;
}
