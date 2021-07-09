using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using FSharpUtils.Newtonsoft;
using Microsoft.FSharp.Core;

namespace Tweek.JPad.RuntimeSupport
{
    // this is not a good place for it
    public class Emitter
    {
        private static AssemblyBuilder _assembly;
        private ILGenerator _il;
        private TypeBuilder _closureType;
        private MethodBuilder _invoke;
        private readonly IDictionary<JsonValue, int> _valuesCache = new Dictionary<JsonValue, int>();

        private readonly FieldInfo _cachedValuesField =
            typeof(EvaluatorDelegateClosure).GetField(EvaluatorDelegateClosure.ValuesFieldName);
        private readonly FieldInfo _jumpTableField =
            typeof(EvaluatorDelegateClosure).GetField(EvaluatorDelegateClosure.JumpTableFieldName);
        private readonly MethodInfo _jsonValueNoneMethod =
            typeof(FSharpOption<JsonValue>).GetProperty(nameof(FSharpOption<JsonValue>.None))?.GetMethod;
        private readonly MethodInfo _jsonValueSomeMethod =
            typeof(FSharpOption<JsonValue>).GetMethod(nameof(FSharpOption<JsonValue>.Some));
        private readonly MethodInfo _compareMethod =
            typeof(EvaluatorDelegateClosure).GetMethod(nameof(EvaluatorDelegateClosure.Compare));
        private readonly  MethodInfo _inArrayMethod =
            typeof(EvaluatorDelegateClosure).GetMethod(nameof(EvaluatorDelegateClosure.InArray));

        private readonly MethodInfo _contextDelegateInvokeMethod = typeof(ContextDelegate).GetMethod(nameof(ContextDelegate.Invoke));
        private readonly MethodInfo _containsMethod = typeof(EvaluatorDelegateClosure).GetMethod(nameof(EvaluatorDelegateClosure.Contains));
        private readonly MethodInfo _startsWith = typeof(EvaluatorDelegateClosure).GetMethod(nameof(EvaluatorDelegateClosure.StringStartsWith));
        private readonly MethodInfo _endsWith = typeof(EvaluatorDelegateClosure).GetMethod(nameof(EvaluatorDelegateClosure.StringEndsWith));
        private readonly MethodInfo _timeSpanFromMilliseconds = typeof(TimeSpan).GetMethod(nameof(TimeSpan.FromMilliseconds));
        private readonly MethodInfo _withinTime = typeof(EvaluatorDelegateClosure).GetMethod(nameof(EvaluatorDelegateClosure.WithinTime));

        private Emitter()
        {
        }
        
        public static Emitter CreateJPadEvaluatorEmitter(string keyName)
        {
            var emitter = new Emitter();
            _assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("JPAD Rules"),
                AssemblyBuilderAccess.RunAndCollect);
            var mainModule = _assembly.DefineDynamicModule("Main");
            emitter._closureType = mainModule.DefineType(keyName, TypeAttributes.Public | TypeAttributes.Class,
                typeof(EvaluatorDelegateClosure));
            emitter._invoke = emitter._closureType.DefineMethod("Invoke", MethodAttributes.Public| MethodAttributes.Virtual, typeof(FSharpOption<JsonValue>),
                new[] {typeof(ContextDelegate)});
            emitter._il = emitter._invoke.GetILGenerator();
            emitter._invoke.DefineParameter(1, ParameterAttributes.In, "context");
            return emitter;
        }


        public JPadEvaluateExt Finalize( Sha1Provider sha1Provider, IDictionary<string, ComparerDelegate > comparers)
        {
            var cachedValues = this._valuesCache.Keys.OrderBy(k => _valuesCache[k]).ToArray();
            IDictionary<string, int>[] jumpTable = null;
            var paramTypes = new[]
                {typeof(JsonValue[]), typeof(IDictionary<string, int>[]), typeof(Sha1Provider), typeof(IDictionary<string, ComparerDelegate>)};
            var defaultConstructor =
                _closureType.DefineConstructor(MethodAttributes.Public | MethodAttributes.HideBySig,
                    CallingConventions.Standard, paramTypes);
            var dcIL = defaultConstructor.GetILGenerator();
            dcIL.Emit(OpCodes.Ldarg_0);
            dcIL.Emit(OpCodes.Ldarg_1);
            dcIL.Emit(OpCodes.Ldarg_2);
            dcIL.Emit(OpCodes.Ldarg_3);
            dcIL.Emit(OpCodes.Ldarg_S, 4);
            dcIL.Emit(OpCodes.Call,
                typeof(EvaluatorDelegateClosure).GetConstructor(paramTypes) ??
                throw new Exception("Cannot find constructor"));
            dcIL.Emit(OpCodes.Ret);
            
            var theType = _closureType.CreateType();
            
            var constructor = theType.GetConstructor(paramTypes);
            var closure = (EvaluatorDelegateClosure) constructor.Invoke(new object[]{cachedValues, jumpTable, sha1Provider, comparers});
            Debug.Assert(closure != null, nameof(closure) + " != null");
            Save(theType, "test-analysis.dll");
            return closure.Invoke;
        }

        private static void Save(Type typ, string path)
        {
            var generator = new Lokad.ILPack.AssemblyGenerator();
            generator.GenerateAssembly(typ.Assembly, path);
        }

        public void EmitReturnJsonValueSome(JsonValue value, bool useDup = false)
        {
            EmitJsonValue(value, useDup);
            _il.Emit(OpCodes.Call, _jsonValueSomeMethod);
            _il.Emit(OpCodes.Ret);
        }
        
        public void EmitReturnJsonValueNone()
        {
            EmitJsonValueNone();
            _il.Emit(OpCodes.Ret);
        }

        private void EmitJsonValueNone()
        {
            _il.Emit(OpCodes.Call, _jsonValueNoneMethod);
        }

        public void EmitJsonValue(JsonValue value, bool useDup)
        {
            int index;
            if (!_valuesCache.TryGetValue(value, out index))
            {
                index = _valuesCache.Count;
                _valuesCache[value] = index;
            }

            EmitCachedValue(index, useDup);
        }

        private void EmitCachedValue(int index, bool useDup)
        {
            _il.Emit(useDup ? OpCodes.Dup : OpCodes.Ldarg_0);
            _il.Emit(OpCodes.Ldfld, _cachedValuesField);
            _il.Emit(OpCodes.Ldc_I4, index);
            _il.Emit(OpCodes.Ldelem, typeof(JsonValue));
        }

        public Label ReserveLabel()
        {
            return _il.DefineLabel();
        }

        public void MarkLabel(Label label)
        {
            _il.MarkLabel(label);
        }

        public void EmitComparison(string property, EvaluatorDelegateClosure.ComparisonOp operation, JsonValue left,
            string comparisonType)
        {
            _il.Emit(OpCodes.Ldarg_0);
            EmitJsonValue(left, useDup: true);
            EmitFetchContextProperty(property);
            _il.Emit(OpCodes.Ldc_I4, (int) operation);
            if (comparisonType == null)
            {
                _il.Emit(OpCodes.Ldnull);
            }
            else
            {
                _il.Emit(OpCodes.Ldstr, comparisonType);
            }
            _il.Emit(OpCodes.Call, _compareMethod);
        }

        private void EmitFetchContextProperty(string property)
        {
            _il.Emit(OpCodes.Ldarg_1);
            _il.Emit(OpCodes.Ldstr, property);
            _il.Emit(OpCodes.Callvirt, _contextDelegateInvokeMethod);
        }

        public void EmitJumpIfFalse(Label target)
        {
            _il.Emit(OpCodes.Brfalse, target);
        }

        public void EmitJump(Label target)
        {
            _il.Emit(OpCodes.Br, target);
        }
        
        public void EmitInArray(string whatProperty, IEnumerable<JsonValue> values, string comparisonType)
        {
            _il.Emit(OpCodes.Ldarg_0);
            EmitJsonValue(JsonValue.NewArray(values.ToArray()), useDup: true);
            EmitFetchContextProperty(whatProperty);
            if (comparisonType == null)
            {
                _il.Emit(OpCodes.Ldnull);
            }
            else
            {
                _il.Emit(OpCodes.Ldstr, comparisonType);
            }
            _il.Emit(OpCodes.Call, _inArrayMethod);
        }

        public void EmitContains(string contextProperty, JsonValue value, string comparisonType)
        {
            _il.Emit(OpCodes.Ldarg_0);
            EmitJsonValue(value, useDup: true);
            EmitFetchContextProperty(contextProperty);
            if (comparisonType == null)
            {
                _il.Emit(OpCodes.Ldnull);
            }
            else
            {
                _il.Emit(OpCodes.Ldstr, comparisonType);
            }
            _il.Emit(OpCodes.Call, _containsMethod);
        }

        public void EmitStartsWith(string contextProperty, string prefix)
        {
            EmitFetchContextProperty(contextProperty);
            _il.Emit(OpCodes.Ldstr, prefix);
            _il.Emit(OpCodes.Call, _startsWith);
        }
        
        public void EmitEndsWith(string contextProperty, string prefix)
        {
            EmitFetchContextProperty(contextProperty);
            _il.Emit(OpCodes.Ldstr, prefix);
            _il.Emit(OpCodes.Call, _endsWith);
        }

        public void EmitWithinTime(string contextProperty, TimeSpan timeSpan)
        {
            EmitFetchContextProperty(contextProperty);
            EmitFetchContextProperty("system.time_utc");
            _il.Emit(OpCodes.Ldc_R8, timeSpan.TotalMilliseconds);
            _il.Emit(OpCodes.Call, _timeSpanFromMilliseconds);
            _il.Emit(OpCodes.Call, _withinTime);
        }
    }
}