using System.Text.RegularExpressions;
namespace LAC.Domain;
public static class KhasraNumber { public static string Normalize(string value) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Khasra number is required."); return Regex.Replace(value.Trim().ToUpperInvariant(), @"\s+", ""); } }
