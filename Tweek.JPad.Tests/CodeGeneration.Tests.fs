﻿module CodeGenerationTests

open FsUnit
open Tweek.JPad.CodeGeneration
open Xunit
open FsCheck.Xunit;
open FSharpUtils.Newtonsoft;
open Microsoft.FSharp.Reflection;
open Newtonsoft.Json
open Tweek.JPad
open FsCheck
open System
open Tests.Common

type ``Code Generation tests`` () =
    let parser = CodeGeneration.GenerateDelegate (ParserSettings(defaultSha1Provider, dict([("version", new ComparerDelegate(fun x -> Version.Parse(x) :> IComparable))]))) "test_key"
    let createContext seq = ContextDelegate(fun name -> seq |> Seq.tryFind (fun (k,v)->k = name) |> Option.map (fun (k,v)->JsonValue.String v))

    let validate (rules:JPadEvaluateExt) context value =
        let result = rules.Invoke context
        result |> should equal value

    let validateValue (rules:JPadEvaluateExt) context value = validate rules context (Some(JsonValue.String value))
    let validateNone (rules:JPadEvaluateExt) context = validate rules context Option.None

    [<Fact>]
    member test.``Use partitions with simple values``() =
        let rules = parser <| """
        {
            "partitions": [
                "fruit",
                "cultivar"
            ],
            "rules": {
                "apple": {
                    "smith": "green",
                    "*": "red"
                },
                "banana": "yellow",
                "*": "unknown"
            }
        }"""
        validateValue rules (createContext [("fruit", "Apple");("cultivar", "smith");]) "green"
        validateValue rules (createContext [("fruit", "apple");("cultivar", "granny");]) "red"
        validateValue rules (createContext [("fruit", "apple")]) "red"
        validateValue rules (createContext [("fruit", "banana")]) "yellow"
        validateValue rules (createContext [("fruit", "grapes")]) "unknown"
        validateValue rules (createContext []) "unknown"

    [<Fact>]
    member test.``Use partitions with full rules``() =
        let rules = parser <| """
        {
            "partitions": [
                "fruit"
            ],
            "rules": {
                "apple": [
                    {
                        "Matcher": {
                            "cultivar": "smith"
                        },
                        "Value": "green",
                        "Type": "SingleVariant"
                    },
                    {
                        "Matcher": { },
                        "Type": "SingleVariant",
                        "Value": "red"
                    }
                ],
                "banana": "yellow",
                "*": "unknown"
            }
        }"""
        validateValue rules (createContext [("fruit", "apple");("cultivar", "smith");]) "green"
        validateValue rules (createContext [("fruit", "apple");("cultivar", "granny");]) "red"
        validateValue rules (createContext [("fruit", "apple")]) "red"
        validateValue rules (createContext [("fruit", "banana")]) "yellow"
        validateValue rules (createContext [("fruit", "grapes")]) "unknown"
        validateValue rules (createContext []) "unknown"

    [<Fact>]
    member test.``Use partitions with full rules and default value``() =
        let rules = parser <| """
        {
            "partitions": [
                "fruit"
            ],
            "defaultValue": "unknown",
            "rules": {
                "apple": [
                    {
                        "Matcher": {
                            "cultivar":"smith"
                        },
                        "Value": "green",
                        "Type": "SingleVariant"
                    },
                    {
                        "Matcher": { },
                        "Type": "SingleVariant",
                        "Value": "red"
                    }
                ],
                "banana": "yellow"
            }
        }"""
        validateValue rules (createContext [("fruit", "apple");("cultivar", "smith");]) "green"
        validateValue rules (createContext [("fruit", "apple");("cultivar", "granny");]) "red"
        validateValue rules (createContext [("fruit", "apple")]) "red"
        validateValue rules (createContext [("fruit", "banana")]) "yellow"
        validateValue rules (createContext [("fruit", "grapes")]) "unknown"
        validateValue rules (createContext []) "unknown"



    [<Fact>]
    member test.``Use full rules and default value``() =
        let rules = parser <| """
        {
            "partitions": [ ],
            "defaultValue": "unknown",
            "rules": [
                {
                    "Matcher": {
                        "fruit": "apple",
                        "cultivar": "smith"
                    },
                    "Value": "green",
                    "Type": "SingleVariant"
                },
                {
                    "Matcher": {
                        "fruit": "apple"
                    },
                    "Type": "SingleVariant",
                    "Value": "red"
                },
                {
                    "Matcher": {
                        "fruit": "banana"
                    },
                    "Type": "SingleVariant",
                    "Value": "yellow"
                }
            ]
        }"""
        validateValue rules (createContext [("fruit", "apple");("cultivar", "smith");]) "green"
        validateValue rules (createContext [("fruit", "apple");("cultivar", "granny");]) "red"
        validateValue rules (createContext [("fruit", "apple")]) "red"
        validateValue rules (createContext [("fruit", "banana")]) "yellow"
        validateValue rules (createContext [("fruit", "grapes")]) "unknown"
        validateValue rules (createContext []) "unknown"


    [<Fact>]
    member test.``Use full rules without default value``() =
        let rules = parser <| """
        {
            "partitions": [ ],
            "rules": [
                {
                    "Matcher": {
                        "fruit": "apple",
                        "cultivar": "smith"
                    },
                    "Value": "green",
                    "Type": "SingleVariant"
                },
                {
                    "Matcher": {
                        "fruit": "apple"
                    },
                    "Type": "SingleVariant",
                    "Value": "red"
                },
                {
                    "Matcher": {
                        "fruit": "banana"
                    },
                    "Type": "SingleVariant",
                    "Value": "yellow"
                }
            ]
        }"""
        validateValue rules (createContext [("fruit", "apple");("cultivar", "smith");]) "green"
        validateValue rules (createContext [("fruit", "apple");("cultivar", "granny");]) "red"
        validateValue rules (createContext [("fruit", "apple")]) "red"
        validateValue rules (createContext [("fruit", "banana")]) "yellow"
        validateNone rules (createContext [("fruit", "grapes")])
        validateNone rules (createContext [])

    [<Fact>]
    member test.``Use partitioned key with invalid rules``() =
        let invalidJPad = """
        {
            "partitions": [
                "fruit",
                "cultivar"
            ],
            "rules": {
                "apple": {
                    "*": [
                        {
                            "Matcher": {
                                "device.Version": {
                                    "$compare": "version",
                                    "$ge": "abcd"
                                }
                            },
                            "Type": "SingleVariant",
                            "Value": "abcd"
                        }
                    ]
                }
            }
        }"""
        (fun () -> (parser <| invalidJPad) |> ignore) |> should throw typeof<ParseError>