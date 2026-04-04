using System;
using System.Reflection;
using System.Linq;

var asm = typeof(Microsoft.ML.Tokenizers.SentencePieceTokenizer).Assembly;
var spType = asm.GetType(""Microsoft.ML.Tokenizers.SentencePieceTokenizer"");
if (spType == null) { Console.WriteLine(""Type not found""); return; }

var createMethods = spType.GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Where(m => m.Name == ""Create"");

foreach (var m in createMethods)
{
    var ps = m.GetParameters();
    Console.WriteLine($""Create({string.Join("", "", ps.Select(p => $""{p.ParameterType.Name} {p.Name}""))})"");
}
