using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
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
            Func<string, string, int> stringComparer = MakeComparer(comparisonType);
            var actualRight = right?.Value ?? JsonValue.Null;
            
            if (left.IsNull && actualRight.IsNull)
            {
                return op == ComparisonOp.Equal;
            }
            
            if (left.IsNull || actualRight.IsNull)
            {
                return false;
            }
            
            if (left.IsString && actualRight.IsString)
            {
                    return CompareToOp(stringComparer(left.AsString(), actualRight.AsString()), op);
            }

            if (left.IsNumber)
            {
                return CompareToOp(left.AsDecimal().CompareTo(actualRight.AsDecimal()), op);
            }

            if (left.IsFloat)
            {
                return CompareToOp(left.AsFloat().CompareTo(actualRight.AsFloat()), op);
            }

            if (left.IsBoolean)
            {
                return CompareToOp(left.AsBoolean().CompareTo(actualRight.AsBoolean()), op);
            }

            throw new Exception("No matching types");
        }

        private Func<string, string, int> MakeComparer(string comparisonType) => comparisonType == null
            ? CompareAuto
            : (a, b) => CompareCustom(a, b, comparisonType);

        public int CompareAuto(string left, string right) =>
            string.Compare(left, right, StringComparison.OrdinalIgnoreCase);

        public int CompareCustom(string left, string right, string comparisonType)
        {
            return comparers[comparisonType].Invoke(left).CompareTo(right);
        }

        public bool InArray(JsonValue values, JsonValue what, string comparisonType) =>
            values.AsArray().Any(value => Compare(value, FSharpOption<JsonValue>.Some(what), ComparisonOp.Equal, comparisonType));
    }
}