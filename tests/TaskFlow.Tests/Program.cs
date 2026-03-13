using System;
using System.Collections.Generic;
using TaskFlow.Helpers;
using TaskFlow.Models.TaskCards;

namespace TaskFlow.Tests
{
    class Program
    {
        [STAThread]
        static void Main()
        {
            
            Console.WriteLine("Running Expression Evaluation Tests...");

            bool allTestsPassed = true;

            // 1. Simple Equality
            allTestsPassed &= Test("1 == 1", true);
            allTestsPassed &= Test("1 != 2", true);
            allTestsPassed &= Test("1 > 2", false);

            // 2. String Equality
            allTestsPassed &= Test("\"ABC\" == \"ABC\"", true);
            allTestsPassed &= Test("\"ABC\" == \"ABD\"", false);
            allTestsPassed &= Test("\"ABC\" != \"ABD\"", true);

            // 3. String Containment
            allTestsPassed &= Test("\"ABCDE\" contains \"ABC\"", true);
            allTestsPassed &= Test("\"ABCDE\" 包含 \"ABC\"", true);
            allTestsPassed &= Test("\"ABCDE\" contains \"F\"", false);
            allTestsPassed &= Test("\"ABCDE\" 包含 \"F\"", false);
            // New operator =~
            allTestsPassed &= Test("\"ABCDE\" =~ \"ABC\"", true);
            allTestsPassed &= Test("\"ABCDE\" =~ \"F\"", false);

            // 4. Output Text Reference
            var tasks = new List<TaskCardBase>
            {
                new ImgOcrTaskCard { Order = 1, OutputText = "ABC" },
                new ImgOcrTaskCard { Order = 2, OutputText = "DEF" }
            };

            // Setup task names for reference
            tasks[0].Name = "OCR识别";
            tasks[1].Name = "OCR识别2";

            allTestsPassed &= TestResolve("#1 OCR识别.文本 == \"ABC\"", tasks, true);
            allTestsPassed &= TestResolve("#1 OCR识别.文本 contains \"A\"", tasks, true);
            allTestsPassed &= TestResolve("#1 OCR识别.文本 =~ \"A\"", tasks, true);
            allTestsPassed &= TestResolve("#1 OCR识别.文本 == \"DEF\"", tasks, false);

            // 5. Output Text Reference Alias
            allTestsPassed &= TestResolve("#1 OCR识别.输出文本 == \"ABC\"", tasks, true);


            // 6. Multiline String
            allTestsPassed &= Test("\"Line1\\nLine2\" == \"Line1\\nLine2\"", true);
            allTestsPassed &= Test("\"Line1\\r\\nLine2\" == \"Line1\\r\\nLine2\"", true);

            // 7. Multiline Expression Layout
            allTestsPassed &= Test("\"A\"\n==\n\"A\"", true);

            // 8. Logical Operators
            // OR (||)
            allTestsPassed &= Test("\"ABC\" == \"ABC\" || 1 < 2", true);
            allTestsPassed &= Test("\"ABC\" == \"DEF\" || 1 < 2", true);
            allTestsPassed &= Test("\"ABC\" == \"DEF\" || 1 > 2", false);
            allTestsPassed &= Test("1 == 1 || 2 == 2", true);

            // AND (&&)
            allTestsPassed &= Test("\"ABC\" == \"ABC\" && 1 == 1", true);
            allTestsPassed &= Test("\"ABC\" == \"ABC\" && 1 == 2", false);
            allTestsPassed &= Test("1 == 1 && 2 == 2", true);

            // Mixed (precedence: AND binds tighter, but we implement left-to-right top-level split for OR first)
            // A || B && C  -> A || (B && C)
            // True || False -> True
            allTestsPassed &= Test("1 == 1 || 1 == 2 && 1 == 1", true);

            // (A || B) && C is not directly supported by simple split unless we implement parentheses or recursive parsing carefully.
            // For now, let's stick to the requirement: "ABC"=="ABC"||1<2 and "ABC"=~"A"&&1==1

            // Complex with existing operators
            allTestsPassed &= Test("\"ABC\" =~ \"A\" && 1 == 1", true);
            allTestsPassed &= Test("\"ABC\" contains \"B\" || 1 > 2", true);


            if (allTestsPassed)
            {
                Console.WriteLine("\nAll tests passed!");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("\nSome tests failed.");
                Environment.Exit(1);
            }
        }

        static bool Test(string expression, bool expected)
        {
            try
            {
                bool result = ExpressionEvaluator.Evaluate(expression);
                if (result == expected)
                {
                    Console.WriteLine($"[PASS] {expression} => {result}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[FAIL] {expression} => {result} (Expected: {expected})");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] {expression} => Exception: {ex.Message}");
                return false;
            }
        }

        static bool TestResolve(string expressionWithRef, IList<TaskCardBase> tasks, bool expected)
        {
            try
            {
                string resolved = ExpressionEvaluator.ResolveExpression(expressionWithRef, tasks);
                Console.WriteLine($"Resolved: {expressionWithRef} => {resolved}");
                bool result = ExpressionEvaluator.Evaluate(resolved);
                if (result == expected)
                {
                    Console.WriteLine($"[PASS] {expressionWithRef} => {result}");
                    return true;
                }
                else
                {
                    Console.WriteLine($"[FAIL] {expressionWithRef} => {result} (Expected: {expected})");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] {expressionWithRef} => Exception: {ex.Message}");
                return false;
            }
        }

    }
}
