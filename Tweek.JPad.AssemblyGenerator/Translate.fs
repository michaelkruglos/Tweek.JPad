module Translate

open Mono.Cecil
open Mono.Cecil.Cil
open Mono.Cecil.Rocks
open System.IO
open System
open System.Linq
open System.Collections.Generic
open System.Collections.Immutable
open Tweek.JPad.AST
open Tweek.JPad
open FSharpUtils.Newtonsoft

type private KnownMethods(mainModule:ModuleDefinition) =
    member _.FetchContext = mainModule.ImportReference(typeof<ContextDelegate>.GetMethod("Invoke"))
    member _.StringStartsWith = mainModule.ImportReference(typeof<String>.GetMethod("StartsWith", Array.create(1)(typeof<String>)))
    member _.StringEndsWith = mainModule.ImportReference(typeof<String>.GetMethod("EndsWith", Array.create(1)(typeof<String>)))
    member _.JsonValue_Parse = mainModule.ImportReference(typeof<JsonValue>.GetMethod("Parse"))
    member _.JsonValueAsString = mainModule.ImportReference(typeof<JsonExtensions>.GetMethod("AsString"))
    member _.CompareTo = mainModule.ImportReference(typeof<JsonValue>.GetMethod("CompareTo", Array.create(1)(typeof<JsonValue>)))
    member _.OptionJsonValue_get_Value = mainModule.ImportReference(typeof<Option<JsonValue>>.GetMethod("get_Value"))
    member _.OptionJsonValue_Some = mainModule.ImportReference(typeof<Option<JsonValue>>.GetConstructor(Array.singleton typeof<JsonValue>))

type Translator(name: string, version: Version, ?mainModuleName: string) =
    let assembly = 
        AssemblyDefinition.CreateAssembly(AssemblyNameDefinition(name, version), (defaultArg mainModuleName name), ModuleKind.Console)

    let mainModule = assembly.MainModule

    let typeMap = 
        // This is necessary to ensure we're generating NETCore App assembly
        let systemRuntimeDllPath = Path.Combine(Path.GetDirectoryName(typeof<System.Object>.Assembly.GetFiles().Single().Name), "System.Runtime.dll")
        let systemRuntimeAssembly = AssemblyDefinition.ReadAssembly(systemRuntimeDllPath)
        mainModule.AssemblyReferences.Add(systemRuntimeAssembly.Name)
        let stdtypes = dict[
                typeof<Object>, mainModule.TypeSystem.Object
                typeof<Void>,   mainModule.TypeSystem.Void
                typeof<String>, mainModule.TypeSystem.String
            ]
        let result = stdtypes.ToDictionary((fun x->x.Key), (fun x -> x.Value))
        let extratypes = [
                typeof<ContextDelegate> 
                typeof<JsonValue> 
                typeof<Option<JsonValue>> 
            ]
        extratypes |> List.iter(fun t -> result.Add(t, assembly.MainModule.ImportReference(t)))
        result.ToImmutableDictionary()

    do printfn "%s" (typeof<Object>.Assembly.GetFiles() |> Seq.map(fun f -> f.Name) |> (fun names -> String.Join("\n", names)))

    let keysType = TypeDefinition("Tweek", "Keys", TypeAttributes.Class|||TypeAttributes.Public, typeMap.[typeof<Object>])

    do mainModule.Types.Add(keysType)

    let methodMap = KnownMethods(mainModule)

    let keyEvaluators = Dictionary<string, MethodReference>()
    let jsonValues = Dictionary<JsonValue, FieldReference>()
    let fixupPoints = List<(int * int)>()


    member self.writeAssemblyTodisk(?outputDirectoryPath: string) =
        self.emitClassConstructor()
        self.emitConstructor()
        self.emitJPadEvaluator()

        let ensureDirectory path = if Directory.Exists(path) then Directory.CreateDirectory(path) |> ignore
        let v = outputDirectoryPath |> Option.bind (fun path ->
            if Directory.Exists(path) then path
            else Directory.CreateDirectory(path).FullName)
            |> (Option.defaultValue assembly.Name.Name)
        let pathWithoutExtension =
            match outputDirectoryPath with
            | Some path ->
                    ensureDirectory path
                    if not(Directory.Exists(path)) then Directory.CreateDirectory(path) |> ignore
                    Path.Combine(outputDirectoryPath.Value, assembly.Name.Name)
            | None -> assembly.Name.Name
        let dllName = Path.ChangeExtension(pathWithoutExtension, ".dll")
        assembly.Write(dllName)
        (*
        let runtimeconfigName = Path.ChangeExtension(pathWithoutExtension, ".runtimeconfig.json")
        File.WriteAllText(runtimeconfigName, """
        {
          "runtimeOptions": {
            "tfm": "netcoreapp3.1",
            "framework": {
              "name": "Microsoft.NETCore.App",
              "version": "3.1.0"
            }
          }
        }
        """)
        *)

    member _.emitClassConstructor() =
        let cctorAttributes = MethodAttributes.Static|||MethodAttributes.Public|||MethodAttributes.RTSpecialName|||MethodAttributes.SpecialName|||MethodAttributes.HideBySig
        let cctor = MethodDefinition(".cctor", cctorAttributes, typeMap.[typeof<Void>])
        let ilProcessor = cctor.Body.GetILProcessor()
        let baseCctor = keysType.BaseType.Resolve().GetStaticConstructor()
        if baseCctor <> null then
            ilProcessor.Emit(OpCodes.Ldarg_0)
            ilProcessor.Emit(OpCodes.Call, mainModule.ImportReference(baseCctor))
        for kv in jsonValues do
            let str = JsonHelper.stringify kv.Key false 
            ilProcessor.Emit(OpCodes.Ldstr, str)
            ilProcessor.Emit(OpCodes.Call, methodMap.JsonValue_Parse)
            ilProcessor.Emit(OpCodes.Stsfld, kv.Value)

        ilProcessor.Emit(OpCodes.Ret)
        ilProcessor.Body.Optimize()
        keysType.Methods.Add(cctor)

    member _.emitConstructor() =
        let ctorAttributes = MethodAttributes.Public|||MethodAttributes.RTSpecialName|||MethodAttributes.SpecialName|||MethodAttributes.HideBySig
        let ctor = MethodDefinition(".ctor", ctorAttributes, typeMap.[typeof<Void>])
        let ilProcessor = ctor.Body.GetILProcessor()
        let baseCtor = mainModule.ImportReference(keysType.BaseType.Resolve().GetConstructors().Single(fun m -> m.Parameters.Count = 0))
        ilProcessor.Emit(OpCodes.Ldarg_0)
        ilProcessor.Emit(OpCodes.Call, baseCtor)
        ilProcessor.Emit(OpCodes.Ret)
        ilProcessor.Body.Optimize()
        keysType.Methods.Add(ctor)

    member self.emitJPadEvaluator() =
        ()

    member self.emitMethodForKey(key: string, jpad: JPad) =
        let method = MethodDefinition(key, MethodAttributes.Public, typeMap.[typeof<Option<JsonValue>>])
        ParameterDefinition("getContext", ParameterAttributes.None, typeMap.[typeof<ContextDelegate>]) |> method.Parameters.Add
        let processor = method.Body.GetILProcessor()
        printfn "Generating %s" key
        match jpad.Rules with
        | RulesByPartition (partitionType, rules, defaultRules) ->  
            printfn "partitions %s" partitionType
        | RulesList rules -> 
            rules |> Seq.iter(fun r ->
                printfn "Processing rule %A" r
                let matcher = fst r
                let value = snd r
                let trueCase = processor.Create(OpCodes.Nop)
                let falseCase = processor.Create(OpCodes.Nop)
                self.emitExpression processor "" matcher trueCase falseCase
                processor.Append(trueCase)
                self.emitRuleValue processor value key
                processor.Emit(OpCodes.Newobj, methodMap.OptionJsonValue_Some)
                processor.Emit(OpCodes.Ret)
                processor.Append(falseCase)
                printfn "DONE!"
                printfn ""
            )
            // default value
        match jpad.DefaultValue with
        | Some v -> 
            self.emitValue processor v
            processor.Emit(OpCodes.Newobj, methodMap.OptionJsonValue_Some)
        | None ->
            processor.Emit(OpCodes.Ldnull)

        processor.Emit(OpCodes.Ret)

        method.Body.OptimizeMacros()
        method.Body.Optimize()
        keysType.Methods.Add(method)
        keyEvaluators.Add(key, assembly.MainModule.ImportReference(method))

    member self.emitRuleValue processor value key =
        match value with
        | SingleVariant v ->
            printfn "Caching value %s" (JsonHelper.stringify v true)
            self.emitValue processor v

        | MultiVariant vd -> failwith "Multivariant is not supported"

    member _.emitValue processor value =
        if not(jsonValues.ContainsKey(value)) then
            let field = FieldDefinition(sprintf "$JsonValue_%i" jsonValues.Count, FieldAttributes.InitOnly ||| FieldAttributes.Static, typeMap.[typeof<JsonValue>])
            keysType.Fields.Add(field)
            jsonValues.Add(value, mainModule.ImportReference(field))
        let fieldRef = jsonValues.[value]
        //processor.Emit(OpCodes.Ldarg_0)
        processor.Emit(OpCodes.Ldsfld, fieldRef)

    static member private getPropName prefix propName =
        if prefix = "" then propName else (prefix + "." + propName)

    member private self.emitExpression processor prefix expr trueCase falseCase =
        printfn "Emitting expression %A for prefix '%s'" expr prefix
        match expr with
        | Empty ->
            processor.Emit(OpCodes.Ldc_I4_1) // 1 is true
        | Property (prop, innerexp) ->
            self.emitExpression processor (Translator.getPropName prefix prop) innerexp trueCase falseCase
        | Op operation ->
            match operation with
            | Op.Not expr ->
                self.emitExpression processor prefix expr falseCase trueCase
            | ConjuctionOp (conjuctionOp, left, right) ->
                printfn "EMITTING CONJUNCTION!!!!!! %s" (conjuctionOp.ToString())
                let nextExpression = processor.Create(OpCodes.Nop)
                match conjuctionOp with
                | And ->
                    self.emitExpression processor prefix left nextExpression falseCase
                    processor.Append(nextExpression)
                    self.emitExpression processor prefix right trueCase falseCase
                | Or ->
                    self.emitExpression processor prefix left trueCase nextExpression
                    processor.Append(nextExpression)
                    self.emitExpression processor prefix right trueCase falseCase
            | StringOp (stringOp, str) ->
                self.emitFetchProperty processor prefix
                let nop = processor.Create(OpCodes.Nop)
                let some = processor.Create(OpCodes.Call, methodMap.JsonValueAsString)
                processor.Emit(OpCodes.Brtrue, some)
                processor.Emit(OpCodes.Ldc_I4_0)
                processor.Emit(OpCodes.Br, nop)
                processor.Append(some)
                processor.Emit(OpCodes.Ldstr, str)
                match stringOp with
                | StartsWith ->
                    processor.Emit(OpCodes.Callvirt, methodMap.StringStartsWith)
                | EndsWith ->
                    processor.Emit(OpCodes.Callvirt, methodMap.StringEndsWith)
                processor.Append(nop)
            | CompareOp (compareOp, value, ctype) ->
                let getValue = processor.Create(OpCodes.Callvirt, methodMap.OptionJsonValue_get_Value)
                self.emitFetchProperty processor prefix
                processor.Emit(OpCodes.Dup)
                processor.Emit(OpCodes.Brtrue, getValue)
                processor.Emit(OpCodes.Pop)
                processor.Emit(OpCodes.Br, falseCase)
                processor.Append(getValue)
                self.emitValue processor value
                processor.Emit(OpCodes.Callvirt, methodMap.CompareTo)
                let nextBranch = match compareOp with
                                 | Equal ->
                                     processor.Emit(OpCodes.Brfalse, trueCase)
                                     falseCase
                                 | NotEqual ->
                                     processor.Emit(OpCodes.Brfalse, falseCase)
                                     trueCase
                                 | GreaterThan ->
                                     processor.Emit(OpCodes.Ldc_I4_0)
                                     processor.Emit(OpCodes.Cgt)
                                     processor.Emit(OpCodes.Brtrue, trueCase)
                                     falseCase
                                 | LessThan ->
                                     processor.Emit(OpCodes.Ldc_I4_0)
                                     processor.Emit(OpCodes.Clt)
                                     processor.Emit(OpCodes.Brtrue, trueCase)
                                     falseCase
                                 | GreaterEqual ->
                                     processor.Emit(OpCodes.Clt)
                                     processor.Emit(OpCodes.Brfalse, trueCase)
                                     falseCase
                                 | LessEqual ->
                                     processor.Emit(OpCodes.Cgt)
                                     processor.Emit(OpCodes.Ldc_I4_1)
                                     processor.Emit(OpCodes.Ceq)
                                     processor.Emit(OpCodes.Brfalse, trueCase)
                                     falseCase
                processor.Emit(OpCodes.Br, nextBranch)
            | In (comparisonValues, comparisonType) -> raise (NotImplementedException())
            | TimeOp (timeOp, timeSpan) -> raise (NotImplementedException())
            | ContainsOp (comparisonValue, comparisonType) -> raise (NotImplementedException())


    member private self.emitCompareOp processor compareOp value =
        self.emitValue processor value
        processor.Emit(OpCodes.Callvirt, methodMap.CompareTo)
        processor.Emit(OpCodes.Ldc_I4_0)
        match compareOp with
        | Equal ->
            processor.Emit(OpCodes.Ceq)
        | NotEqual ->
            processor.Emit(OpCodes.Ldc_I4_1)
            processor.Emit(OpCodes.Ceq)
        | GreaterThan ->
            processor.Emit(OpCodes.Cgt)
        | LessThan ->
            processor.Emit(OpCodes.Clt)
        | GreaterEqual ->
            processor.Emit(OpCodes.Clt)
            processor.Emit(OpCodes.Ldc_I4_1)
            processor.Emit(OpCodes.Ceq)
        | LessEqual ->
            processor.Emit(OpCodes.Cgt)
            processor.Emit(OpCodes.Ldc_I4_1)
            processor.Emit(OpCodes.Ceq)

    member private _.emitFetchProperty (processor:ILProcessor) (propertyName: string) =
        processor.Emit(OpCodes.Ldarg_1)
        processor.Emit(OpCodes.Ldstr,propertyName)
        processor.Emit(OpCodes.Callvirt, methodMap.FetchContext)


