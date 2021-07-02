namespace Tweek.JPad.CodeGeneration

open System.Reflection.Emit
open FSharpUtils.Newtonsoft
open Tweek.JPad
open Tweek.JPad.AST
open Tweek.JPad.RuntimeSupport

module CodeGeneration =
    
    let comparisonTypeToString (comparison:ComparisonType) =
        match comparison with
        |Auto -> null
        |Custom customType -> customType

    let rec compileMatcher (emitter: Emitter) (shortcut: Label) (prefix: string) (matcher: MatcherExpression) =
        match matcher with
        |Empty -> ()
        |Property (propertyName, matcherExpression) ->
            let newPrefix = (if prefix = null then propertyName else prefix + "." + propertyName)
            compileMatcher emitter shortcut newPrefix matcherExpression
        |Op operation ->
            match operation with
            |ConjuctionOp (conjunction, matcherLeft, matcherRight) ->
                match conjunction with
                |And ->
                    compileMatcher emitter shortcut prefix matcherLeft
                    compileMatcher emitter shortcut prefix matcherRight
                |Or ->
                    let tryRightMatcher = emitter.ReserveLabel()
                    compileMatcher emitter tryRightMatcher prefix matcherLeft
                    emitter.MarkLabel(tryRightMatcher)
                    compileMatcher emitter shortcut prefix matcherRight
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
            |In (values,newComparisonType) -> emitter.EmitInArray(prefix, values, (comparisonTypeToString newComparisonType))
            |TimeOp (op, value) -> ()
            |StringOp (op, value) -> ()
            |ContainsOp (value, newComparisonType) -> ()
                

    let compileRule (emitter: Emitter) (defaultValue: Label) (matcher: MatcherExpression,value:RuleValue) =
        compileMatcher emitter defaultValue null matcher
        //emitter.EmitJumpIfFalse(defaultValue);
        match value with
        |SingleVariant jsonValue ->
            emitter.EmitReturnJsonValueSome jsonValue
        |MultiVariant valueDistribution ->
            match valueDistribution.DistributionType with
            |Uniform uniformValues -> ()
            |Weighted weightedValues -> ()
            |Bernouli fp -> ()
        
    let rec compileRulesContainer (emitter: Emitter) container shortcut =
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
        
    let compile (emitter: Emitter) (jpad: JPad) =
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
