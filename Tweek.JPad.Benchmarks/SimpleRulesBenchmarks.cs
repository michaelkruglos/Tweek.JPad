using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using FSharpUtils.Newtonsoft;
using Microsoft.FSharp.Core;

namespace Tweek.JPad.Benchmarks
{
    [BenchmarkCategory("Simple")]
    public class SimpleRulesBenchmarks: Common
    {
        private const string Rules = @"{
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
        }";

        private static readonly IDictionary<string, ContextDelegate> TestCases = new Dictionary<string, ContextDelegate>
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
        
        
        public SimpleRulesBenchmarks(): base(TestCases, Rules)
        {
        }
    }
}