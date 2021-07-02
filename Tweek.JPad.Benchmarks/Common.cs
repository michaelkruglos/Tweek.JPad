using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using FSharpUtils.Newtonsoft;

namespace Tweek.JPad.Benchmarks
{
    public class Common
    {
        protected readonly JPadEvaluateExt _defaultEvaluator;
        protected readonly JPadEvaluateExt _codeGenerationEvaluator;
        protected IDictionary<string, ContextDelegate> _testCases;
        
        public Common(IDictionary<string, ContextDelegate> testCases, string rules)
        {
            _testCases = testCases;
            var comparers = new Dictionary<string, ComparerDelegate> {{"version", Version.Parse}};
            var settings = new ParserSettings(SHA1.HashData, comparers);
            var parser = new JPadParser(settings);
            _defaultEvaluator = parser.Parse.Invoke(rules);
            _codeGenerationEvaluator = CodeGeneration.CodeGeneration.GenerateDelegate(settings, "test_key", rules);
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