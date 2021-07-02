using System;
using BenchmarkDotNet.Running;

namespace Tweek.JPad.Benchmarks
{
    class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<SimpleRules>();
        }
    }
}
