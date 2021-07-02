using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using FSharpUtils.Newtonsoft;
using Microsoft.FSharp.Core;

namespace Tweek.JPad.Benchmarks
{
    public class SimpleRules
    {
        private JPadEvaluateExt _defaultEvaluator;
        private JPadEvaluateExt _codeGenerationEvaluator;
        private IDictionary<string, ContextDelegate> _testCases;
        private string _rules = @"
        {
            ""partitions"": [ ],
            ""defaultValue"": ""unknown"",
            ""rules"": [
                {
                    ""Matcher"": {
                        ""fruit"": ""apple"",
                        ""cultivar"": ""smith""
                    },
                    ""Value"": ""green"",
                    ""Type"": ""SingleVariant""
                },
                {
                    ""Matcher"": {
                        ""fruit"": ""apple""
                    },
                    ""Type"": ""SingleVariant"",
                    ""Value"": ""red""
                },
                {
                    ""Matcher"": {
                        ""fruit"": ""banana""
                    },
                    ""Type"": ""SingleVariant"",
                    ""Value"": ""yellow""
                }
            ]
        }
";
        
        public SimpleRules()
        {
            Sha1Provider sha1Provider = data => SHA1.HashData(data);
            static IComparable VersionComparer(string version) => Version.Parse(version) as IComparable;
            var comparers = new Dictionary<string, ComparerDelegate> {{"version", VersionComparer}};
            var settings = new ParserSettings(sha1Provider, comparers);
            var parser = new Tweek.JPad.JPadParser(settings);
            _defaultEvaluator = parser.Parse.Invoke(_rules);
            _codeGenerationEvaluator = CodeGeneration.CodeGeneration.GenerateDelegate(settings, "test_key", _rules);
            _testCases = new Dictionary<string, ContextDelegate>
            {
                {
                    "green",
                    item => item switch
                    {
                        "fruit" => FSharpOption<JsonValue>.Some(JsonValue.NewString("apple")),
                        "cultivar" => FSharpOption<JsonValue>.Some(JsonValue.NewString("smith")),
                        _ => FSharpOption<JsonValue>.None
                    }
                },
                {
                    "red",
                    item => item switch
                    {
                        "fruit" => FSharpOption<JsonValue>.Some(JsonValue.NewString("apple")),
                        "cultivar" => FSharpOption<JsonValue>.Some(JsonValue.NewString("granny")),
                        _ => FSharpOption<JsonValue>.None
                    }
                },
                {
                    "yellow",
                    item => item switch
                    {
                        "fruit" => FSharpOption<JsonValue>.Some(JsonValue.NewString("banana")),
                        _ => FSharpOption<JsonValue>.None
                    }
                },
                {
                    "unknown",
                    item => item switch
                    {
                        "fruit" => FSharpOption<JsonValue>.Some(JsonValue.NewString("grapes")),
                        _ => FSharpOption<JsonValue>.None
                    }
                },
            };
        }

        [Benchmark]
        public void DefaultEvaluator()
        {
            RunEvaluator(_defaultEvaluator);
        }

        [Benchmark]
        public void CodeGenerationEvaluator()
        {
            RunEvaluator(_codeGenerationEvaluator);
        }

        private void RunEvaluator(JPadEvaluateExt evaluator)
        {
            foreach (var (result, contextDelegate) in _testCases)
            {
                var value = evaluator(contextDelegate).Value.AsString();
                if (value != result)
                {
                    throw new Exception($"Expected {result}, but got {value}");
                }
            }
        }
    }
}