using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using FSharpUtils.Newtonsoft;
using Microsoft.FSharp.Core;

namespace Tweek.JPad.Benchmarks
{
    [BenchmarkCategory("Partitions")]
    [LogicalGroupColumn]
    public class PartitionRulesBenchmarks: Common
    {
        private const string Rules = @"{
            ""partitions"": [
                ""fruit""
            ],
            ""defaultValue"": ""unknown"",
            ""rules"": {
                ""apple"": [
                    {
                        ""Matcher"": {
                            ""cultivar"":""smith""
                        },
                        ""Value"": ""green"",
                        ""Type"": ""SingleVariant""
                    },
                    {
                        ""Matcher"": { },
                        ""Type"": ""SingleVariant"",
                        ""Value"": ""red""
                    }
                ],
                ""banana"": ""yellow""
            }
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
        
        public PartitionRulesBenchmarks() : base(TestCases, Rules)
        {
        }
    }
}