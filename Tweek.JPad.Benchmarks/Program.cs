using System;
using System.Linq;
using BenchmarkDotNet.Running;

namespace Tweek.JPad.Benchmarks
{
    class Program
    {
        public static void Main(string[] args)
        {
            var benchmarks = typeof(Program).Assembly.DefinedTypes
                .Where(t => t.Name.EndsWith("Benchmarks"))
                .Select(ti => ti.AsType())
                .ToArray();
            var summary = BenchmarkSwitcher.FromTypes(benchmarks);
            summary.RunAllJoined();
        }
    }
}
