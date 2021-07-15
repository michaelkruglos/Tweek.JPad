using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using FSharpUtils.Newtonsoft;
using Microsoft.FSharp.Core;

namespace Tweek.JPad.RuntimeSupport
{
    public abstract class EvaluatorDelegateClosure
    {
        public readonly JsonValue[] cachedValues;
        public readonly KeyValuePair<JsonValue, int>[][] weightedValuesCache;
        public readonly JsonValue[][] uniformValuesCache;
        public readonly Sha1Provider sha1Provider;
        private IDictionary<string, ComparerDelegate> comparers;

        public const string ValuesFieldName = nameof(cachedValues);
        public const string WeightedValuesCacheFieldName = nameof(weightedValuesCache);
        public const string UniformValuesCacheFieldName = nameof(uniformValuesCache);
        public const string Sha1ProviderFieldName = nameof(sha1Provider);
        public const string ComparersFieldName = nameof(comparers);

        public EvaluatorDelegateClosure(JsonValue[] cachedValues, KeyValuePair<JsonValue, int>[][] weightedValuesCache,
            JsonValue[][] uniformValuesCache, Sha1Provider sha1Provider, IDictionary<string, ComparerDelegate> comparers)
        {
            this.cachedValues = cachedValues;
            this.weightedValuesCache = weightedValuesCache;
            this.uniformValuesCache = uniformValuesCache;
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

            var equalityComparer = new JsonValueEqualityComparer(this, comparisonType);
            if (left.IsArray && actualRight.IsArray)
            {
                return left.AsArray().All(item => actualRight.AsArray().Contains(item, equalityComparer));
            }

            if (actualRight.IsArray)
            {
                return actualRight.AsArray().Contains(left, equalityComparer);
            }

            if (!left.IsArray) return false;

            var leftArray = left.AsArray();
            return leftArray.Length == 1 && Compare(leftArray.Single(), right, ComparisonOp.Equal, comparisonType);
        }

        public static bool StringStartsWith(FSharpOption<JsonValue> contextValue, string prefix)
        {
            return contextValue?.Value?.AsString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ?? false;
        }
        
        public static bool StringEndsWith(FSharpOption<JsonValue> contextValue, string prefix)
        {
            return contextValue?.Value?.AsString().EndsWith(prefix,StringComparison.OrdinalIgnoreCase) ?? false;
        }

        public static bool WithinTime(FSharpOption<JsonValue> contextValue, FSharpOption<JsonValue> now,
            TimeSpan within)
        {
            DateTime? referenceTime, timeNow;
            try
            {
                timeNow = DateTime.Parse(now?.Value?.AsString() ?? "");
            }
            catch (Exception _)
            {
                timeNow = null;
            }
            try
            {
                referenceTime = DateTime.Parse(contextValue?.Value?.AsString() ?? "");
            }
            catch (Exception _)
            {
                referenceTime = null;
            }

            if (timeNow != null && referenceTime != null)
            {
                return (referenceTime - timeNow).Value.Duration() < within;
            }

            if (referenceTime == null)
            {
                return false;
            }
            
            if (timeNow == null)
            {
                throw new Exception("Missing system time details");
            }

            return false;
        }

        private static ulong? CalculateHash(Sha1Provider sha1Provider, ContextDelegate context, string ownerType, string salt)
        {
            if (ownerType == null) return null;
            var owner = context(ownerType + ".@@id");
            if (FSharpOption<JsonValue>.get_IsNone(owner)) return null;
            var stringToHash = owner.Value.AsString() + "." + salt;
            return BitConverter.ToUInt64(sha1Provider(Encoding.UTF8.GetBytes(stringToHash)));
        }
        
        public FSharpOption<JsonValue> WeightedDistributionValue(KeyValuePair<JsonValue, int>[] weights, ContextDelegate context, string ownerType, string salt)
        {
            var hash = CalculateHash(sha1Provider, context, ownerType, salt);
            if(hash == null) return FSharpOption<JsonValue>.None;
            var total = (ulong) weights.Sum(pair => pair.Value);
            var selected = (int) (hash % total);
            var totalWeight = 0;
            JsonValue item = null;
            foreach (var (value, weight) in weights)
            {
                totalWeight += weight;
                item = value;
                if (selected < totalWeight) break;
            }

            return FSharpOption<JsonValue>.Some(item);
        }

        public FSharpOption<JsonValue> UniformDistributionValue(JsonValue[] choices, ContextDelegate context, string ownerType, string salt)
        {
            var hash = CalculateHash(sha1Provider, context, ownerType, salt);
            if(hash == null) return FSharpOption<JsonValue>.None;
            var selected = (int)(hash.Value % (ulong)choices.Length);
            return FSharpOption<JsonValue>.Some(choices[selected]);
        }
    }

    internal class JsonValueEqualityComparer: IEqualityComparer<JsonValue>
    {
        private readonly EvaluatorDelegateClosure _delegateClosure;
        private readonly string _comparisonType;
        
        public JsonValueEqualityComparer(EvaluatorDelegateClosure delegateClosure, string comparisonType)
        {
            _delegateClosure = delegateClosure;
            _comparisonType = comparisonType;
        }
        
        public bool Equals(JsonValue x, JsonValue y)
        {
            return _delegateClosure.Compare(x, FSharpOption<JsonValue>.Some(y),
                EvaluatorDelegateClosure.ComparisonOp.Equal, _comparisonType);
        }

        public int GetHashCode(JsonValue obj)
        {
            return HashCode.Combine(obj.Tag, obj.AsString());
        }
    }
}