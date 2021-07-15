namespace Tweek.JPad.CodeGeneration

open System
open System.Collections.Generic
open System.Reflection.Emit
open FSharpUtils.Newtonsoft
open Tweek.JPad
open Tweek.JPad.AST
open Tweek.JPad.RuntimeSupport

module CodeGeneration =
    
    let private comparisonTypeToString (comparison:ComparisonType) =
        match comparison with
        |Auto -> null
        |Custom customType -> customType

    let rec private compileMatcher (emitter: Emitter) (shortcut: Label) (prefix: string) (matcher: MatcherExpression) =
        match matcher with
        |Empty -> ()
        |Property (propertyName, matcherExpression) ->
            let newPrefix = (if prefix = null then propertyName else prefix + "." + propertyName)
            compileMatcher emitter shortcut newPrefix matcherExpression
        |Op operation ->
            match operation with
            |Op.Not matcher ->
                let falseCondition = emitter.ReserveLabel()
                compileMatcher emitter falseCondition prefix matcher
                emitter.EmitJump(shortcut)
                emitter.MarkLabel(falseCondition)
            |ConjuctionOp (conjunction, matcherLeft, matcherRight) ->
                match conjunction with
                |And ->
                    compileMatcher emitter shortcut prefix matcherLeft
                    compileMatcher emitter shortcut prefix matcherRight
                |Or ->
                    let tryRightMatcher = emitter.ReserveLabel()
                    let leftIsEnough = emitter.ReserveLabel()
                    compileMatcher emitter tryRightMatcher prefix matcherLeft
                    emitter.EmitJump(leftIsEnough)
                    emitter.MarkLabel(tryRightMatcher)
                    compileMatcher emitter shortcut prefix matcherRight
                    emitter.MarkLabel(leftIsEnough)
            |CompareOp (comparison,value,comparisonType) ->
                let comparisonOperation = match comparison with
                                          |Equal -> EvaluatorDelegateClosure.ComparisonOp.Equal
                                          |GreaterThan -> EvaluatorDelegateClosure.ComparisonOp.GreaterThan
                                          |LessThan ->EvaluatorDelegateClosure.ComparisonOp.LessThan
                                          |GreaterEqual -> EvaluatorDelegateClosure.ComparisonOp.GreaterEqual
                                          |LessEqual -> EvaluatorDelegateClosure.ComparisonOp.LessEqual
                                          |NotEqual  -> EvaluatorDelegateClosure.ComparisonOp.NotEqual
                                          
                let comparisonTypeString = comparisonTypeToString comparisonType
                emitter.EmitComparison(prefix, comparisonOperation, value, comparisonTypeString)
                emitter.EmitJumpIfFalse(shortcut)
            |In (values,comparisonType) ->
                emitter.EmitInArray(prefix, values, (comparisonTypeToString comparisonType))
                emitter.EmitJumpIfFalse(shortcut)
            |TimeOp (op, value) ->
                match op with
                |WithinTime ->
                    emitter.EmitWithinTime(prefix, value)
                    emitter.EmitJumpIfFalse(shortcut)
                |_ -> raise(NotImplementedException())
            |StringOp (op, value) ->
                match op with
                |StartsWith -> emitter.EmitStartsWith(prefix, value)
                |EndsWith -> emitter.EmitEndsWith(prefix, value)
                emitter.EmitJumpIfFalse(shortcut)
            |ContainsOp (value, comparisonType) ->
                emitter.EmitContains(prefix, value, (comparisonTypeToString comparisonType))
                emitter.EmitJumpIfFalse(shortcut)

    let private compileRule (emitter: Emitter) (defaultValue: Label) (matcher: MatcherExpression,value:RuleValue) =
        compileMatcher emitter defaultValue null matcher
        match value with
        |SingleVariant jsonValue ->
            emitter.EmitReturnJsonValueSome jsonValue
        |MultiVariant valueDistribution ->
            let ownerType = (match valueDistribution.OwnerType with |Some v ->v | None -> null)
            match valueDistribution.DistributionType with
            |Uniform uniformValues ->
                emitter.EmitReturnUniformValue(uniformValues, ownerType, valueDistribution.Salt)
            |Weighted weightedValues ->
                let transformedValues = weightedValues |> Array.map (fun pair -> KeyValuePair(fst pair, snd pair))
                emitter.EmitReturnWeightedValue(transformedValues, ownerType, valueDistribution.Salt)
            |Bernouli fp -> raise(NotImplementedException())
        
    let rec private compileRulesContainer (emitter: Emitter) container shortcut =
        match container with
        |RulesByPartition (partitionProperty, rules, defaultRules) ->
            rules |> Map.iter (fun propertyValue rule ->
                let nextRule = emitter.ReserveLabel()
                emitter.EmitComparison(partitionProperty, EvaluatorDelegateClosure.ComparisonOp.Equal, JsonValue.String(propertyValue), null)
                emitter.EmitJumpIfFalse(nextRule)
                compileRulesContainer emitter rule nextRule
                emitter.MarkLabel(nextRule)
                )
            compileRulesContainer emitter defaultRules shortcut
        |RulesList rules ->
            rules |> List.iter (fun rule ->
                let nextRule = emitter.ReserveLabel()
                compileRule emitter nextRule rule
                emitter.MarkLabel(nextRule)
                )
        
    let private compile (emitter: Emitter) (jpad: JPad) =
        let defaultValue = emitter.ReserveLabel()
        compileRulesContainer emitter jpad.Rules defaultValue
        emitter.MarkLabel(defaultValue)
        match jpad.DefaultValue with
        |Some value -> emitter.EmitReturnJsonValueSome(value)
        |None -> emitter.EmitReturnJsonValueNone()

    let GenerateDelegate (settings: ParserSettings) (keyName: string) (jpad: string) =
        let emitter = Emitter.CreateJPadEvaluatorEmitter(keyName)
        let parser = JPadParser(settings)
        parser.BuildAST jpad
        |> compile emitter
        emitter.Finalize(settings.Sha1Provider, settings.Comparers)
