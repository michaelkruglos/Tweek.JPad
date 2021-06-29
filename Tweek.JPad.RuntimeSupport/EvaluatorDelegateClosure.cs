using System;
using System.Collections.Generic;
using FSharpUtils.Newtonsoft;
using Microsoft.FSharp.Core;

namespace Tweek.JPad.RuntimeSupport
{
    public abstract class EvaluatorDelegateClosure
    {
        public readonly JsonValue[] cachedValues;
        public readonly IDictionary<string, int>[] jumpTable;
        public readonly Sha1Provider sha1Provider;
        private IDictionary<string, ComparerDelegate> comparers;

        public const string ValuesFieldName = nameof(cachedValues);
        public const string JumpTableFieldName = nameof(jumpTable);
        public const string Sha1ProviderFieldName = nameof(sha1Provider);
        public const string ComparersFieldName = nameof(comparers);

        public EvaluatorDelegateClosure(JsonValue[] cachedValues, IDictionary<string, int>[] jumpTable,
            Sha1Provider sha1Provider, IDictionary<string, ComparerDelegate> comparers)
        {
            this.cachedValues = cachedValues;
            this.jumpTable = jumpTable;
            this.sha1Provider = sha1Provider;
            this.comparers = comparers;
        }

        public abstract FSharpOption<JsonValue> Invoke(ContextDelegate contextDelegate);

        public enum ComparisonOp
        {
            Equal,
            GreaterThan,
            LessThan,
            GreaterEqual,
            LessEqual,
            NotEqual
        }

        private static bool CompareToOp(int value, ComparisonOp op)
        {
            return op switch
            {
                ComparisonOp.Equal => value == 0,
                ComparisonOp.GreaterEqual => value >= 0,
                ComparisonOp.LessEqual => value <= 0,
                ComparisonOp.GreaterThan => value >= 0,
                ComparisonOp.LessThan => value < 0,
                ComparisonOp.NotEqual => value != 0,
                _ => throw new ArgumentOutOfRangeException(nameof(op), op, null)
            };
        }

        public bool Compare(JsonValue left, FSharpOption<JsonValue> right, ComparisonOp op, string comparisonType)
        {
            Func<string,string, int> stringComparer = comparisonType == null
                ? CompareAuto
                : (a, b) => CompareCustom(a, b, comparisonType);
            if (left.IsString)
            {
                if (right?.Value?.IsString ?? false)
                {
                    return CompareToOp(stringComparer(left.AsString(), right.Value.AsString()), op);
                }
                return false;
            }
            else
            {
                return false;
            }
        }

        public int CompareAuto(string left, string right) =>
            string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

        public int CompareCustom(string left, string right, string comparisonType)
        {
            return comparers[comparisonType].Invoke(left).CompareTo(right);
        }
    }
}