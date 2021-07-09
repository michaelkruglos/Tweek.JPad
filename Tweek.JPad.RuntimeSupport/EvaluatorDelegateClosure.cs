using System;
using System.Collections;
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
            Func<string, string, int> stringComparer = comparisonType == null
                ? (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase)
                : (a, b) => comparers[comparisonType].Invoke(a).CompareTo(comparers[comparisonType].Invoke(b));

            var actualRight = right?.Value ?? JsonValue.Null;

            if (left.IsNull && actualRight.IsNull)
            {
                return op == ComparisonOp.Equal;
            }

            if (left.IsNull || actualRight.IsNull)
            {
                return false;
            }

            // The order is right to left, see Matcher.fs in JPad
            if (left.IsString && actualRight.IsString)
            {
                return CompareToOp(stringComparer(actualRight.AsString(), left.AsString()), op);
            }

            if (left.IsNumber)
            {
                return CompareToOp(actualRight.AsDecimal().CompareTo(left.AsDecimal()), op);
            }

            if (left.IsFloat)
            {
                return CompareToOp(actualRight.AsFloat().CompareTo(left.AsFloat()), op);
            }

            if (left.IsBoolean)
            {
                return CompareToOp(actualRight.AsBoolean().CompareTo(left.AsBoolean()), op);
            }

            throw new Exception("No matching types");
        }

        private Func<string, string, int> MakeComparer(string comparisonType) => comparisonType == null
            ? (a, b) =>
                string.Compare(a, b, StringComparison.OrdinalIgnoreCase)
            : (a, b) =>
                comparers[comparisonType].Invoke(a).CompareTo(comparers[comparisonType].Invoke(b));

        public bool InArray(JsonValue values, FSharpOption<JsonValue> what, string comparisonType) =>
            values.AsArray().Any(value =>
                Compare(value, what, ComparisonOp.Equal, comparisonType));

        public bool Contains(JsonValue left, FSharpOption<JsonValue> right, string comparisonType)
        {
            var actualRight = right?.Value ?? JsonValue.Null;
            if (left.IsString && actualRight.IsString)
            {
                return actualRight.AsString().ToLower().Contains(left.AsString().ToLower());
            }

            var comparer = new JsonValueEqualityComparer((left, right, comparisonOp) =>
                Compare(left, right, comparisonOp, comparisonType));
            if (left.IsArray && actualRight.IsArray)
            {
                return left.AsArray().All(item => actualRight.AsArray().Contains(item, comparer));
            }

            if (actualRight.IsArray)
            {
                return actualRight.AsArray().Contains(left, comparer);
            }

            if (!left.IsArray) return false;

            var leftArray = left.AsArray();
            return leftArray.Length == 1 && Compare(leftArray.Single(), right, ComparisonOp.Equal, comparisonType);
        }
    }

    internal class JsonValueEqualityComparer: IEqualityComparer<JsonValue>
    {
        private readonly Func<JsonValue, FSharpOption<JsonValue>, EvaluatorDelegateClosure.ComparisonOp, bool> _comparerFunc;
        
        public JsonValueEqualityComparer(Func<JsonValue, FSharpOption<JsonValue>, EvaluatorDelegateClosure.ComparisonOp, bool> comparerFunc)
        {
            _comparerFunc = comparerFunc;
        }
        
        public bool Equals(JsonValue x, JsonValue y)
        {
            return _comparerFunc(x, FSharpOption<JsonValue>.Some(y), EvaluatorDelegateClosure.ComparisonOp.Equal);
        }

        public int GetHashCode(JsonValue obj)
        {
            return HashCode.Combine(obj.Tag, obj.AsString());
        }
    }
}