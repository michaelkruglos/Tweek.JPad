module CodeGenerationTests

open System.Collections
open System.Collections.Generic
open FsUnit
open Tweek.JPad.CodeGeneration
open Xunit
open FSharpUtils.Newtonsoft;
open Tweek.JPad
open System
open Tests.Common
open Xunit

type ContainsOperatorData() as this  =
    inherit TheoryData<string, IEnumerable<KeyValuePair<string,JsonValue>>, bool>()
    let contries1 = [|JsonValue.String("IsrAel");JsonValue.String("Italy");JsonValue.String("Australia")|]
    let contries2 = [|JsonValue.String("IsrAel");JsonValue.String("fRance");JsonValue.String("GermaNy");JsonValue.String("iReland")|]
    let codes1 = [|JsonValue.Number(1m);JsonValue.Number(2m);JsonValue.Number(3m)|]
    let noCountries = [||]
    let expression = """{"Countries": {"$contains": "AustRalia" }}"""
    let emptyExpression = """{"Countries": {"$contains": "" }}"""
    let listExpression= """{"Countries": {"$contains": ["israel","iTaly"] }}"""
    let numberListExpression= """{"Codes": {"$contains": [1] }}"""
    let singleListExpression= """{"Countries": {"$contains": ["israel"] }}"""
    let emptyListExpression = """{"Countries": {"$contains": [] }}"""
    
    do
        this.Add(expression,            [KeyValuePair<string,JsonValue>("Countries", JsonValue.Array(contries1))],     true)
        this.Add(expression,             [KeyValuePair<string,JsonValue>("Countries", JsonValue.Array(contries2))],     false)
        this.Add(expression,             [KeyValuePair<string,JsonValue>("Countries", JsonValue.Array(noCountries))],   false)
        this.Add(expression,             [KeyValuePair<string,JsonValue>("Countries", JsonValue.Null)],                 false)
        this.Add(emptyExpression,        [KeyValuePair<string,JsonValue>("Countries", JsonValue.Array(contries1))],   false)
        this.Add(emptyExpression,        [KeyValuePair<string,JsonValue>("Countries", JsonValue.Array(noCountries))], false)
        this.Add(emptyExpression,        [KeyValuePair<string,JsonValue>("Countries", JsonValue.Null)],               false)
        this.Add(listExpression,         [KeyValuePair<string,JsonValue>("Countries", JsonValue.Array(contries1))],   true)
        this.Add(listExpression,         [KeyValuePair<string,JsonValue>("Countries", JsonValue.Array(contries2))],   false)
        this.Add(listExpression,         [KeyValuePair<string,JsonValue>("Countries", JsonValue.Array(noCountries))], false)
        this.Add(listExpression,         [KeyValuePair<string,JsonValue>("Countries", JsonValue.String("IsrAel"))],   false)
        this.Add(listExpression,         [KeyValuePair<string,JsonValue>("Countries", JsonValue.Null)],               false)
        this.Add(singleListExpression,   [KeyValuePair<string,JsonValue>("Countries", JsonValue.String("IsrAel"))],   true)
        this.Add(singleListExpression,   [KeyValuePair<string,JsonValue>("Countries", JsonValue.String("Isrel"))],    false)
        this.Add(singleListExpression,   [KeyValuePair<string,JsonValue>("Countries", JsonValue.Null)],               false)
        this.Add(emptyListExpression,    [KeyValuePair<string,JsonValue>("Countries", JsonValue.Array(contries1))],   true)
        this.Add(emptyListExpression,    [KeyValuePair<string,JsonValue>("Countries", JsonValue.Array(noCountries))], true)
        this.Add(emptyListExpression,    [KeyValuePair<string,JsonValue>("Countries", JsonValue.Null)],               false)
        this.Add(numberListExpression,   [KeyValuePair<string,JsonValue>("Codes", JsonValue.Array(codes1))],          true)
        this.Add(numberListExpression,   [KeyValuePair<string,JsonValue>("Codes", JsonValue.Null)],                   false)
        
type ``Code Generation tests`` () =
    let versionComparer = ComparerDelegate(fun x -> Version.Parse(x) :> IComparable)
    let dateComparer = ComparerDelegate(fun x -> DateTime.Parse(x) :> IComparable)
    let parser = CodeGeneration.GenerateDelegate (ParserSettings(defaultSha1Provider, dict([("version", versionComparer);("date", dateComparer)]))) "test_key"
    let createContext seq = ContextDelegate(fun name -> seq |> Seq.tryFind (fun (k,v)->k = name) |> Option.map (fun (k,v)->JsonValue.String v))
    let createContextForJsonValue seq = ContextDelegate(fun name -> seq |> Seq.tryFind (fun (k,v)->k = name) |> Option.map snd)

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
    member test.``Rules with custom comparer``() =
        let rules = parser <| """
        {
            "partitions": [],
            "rules": [
                {
                    "Matcher": {
                        "device.Version": {
                            "$compare": "version",
                            "$le": "2.0"
                        }
                    },
                    "Type": "SingleVariant",
                    "Value": "old"
                },
                {
                    "Matcher": {
                        "device.Version": {
                            "$compare": "version",
                            "$gt": "2.0"
                        }
                    },
                    "Type": "SingleVariant",
                    "Value": "new"
                }
            ]
        }"""
        
        validateValue rules (createContext [("device.Version", "1.5");]) "old"
        validateValue rules (createContext [("device.Version", "2.0");]) "old"
        validateValue rules (createContext [("device.Version", "2.1");]) "new"

    member test.``In array operator``() =
        1
    
    [<Fact>]
    member test.``Non-trivial comparison rules``() =
        let rules = parser <| """
        {
            "partitions": [],
            "defaultValue": "no rule",
            "rules": [
                {
                    "Matcher": {
                        "Age": {
                            "$le": 30,
                            "$ge": 25
                        }
                    },
                    "Type": "SingleVariant",
                    "Value": "25 to 30"
                },
                {
                    "Matcher": {
                        "Age": {
                            "$eq": 70
                        }
                    },
                    "Type": "SingleVariant",
                    "Value": "70"
                }
            ]
        }"""
        
        validateValue rules (createContextForJsonValue [("Age", JsonValue.Float(25.0));]) "25 to 30"
        validateValue rules (createContextForJsonValue [("Age", JsonValue.Float(27.0));]) "25 to 30"
        validateValue rules (createContextForJsonValue [("Age", JsonValue.Float(30.0));]) "25 to 30"
        validateValue rules (createContextForJsonValue [("Age", JsonValue.Float(70.0));]) "70"
        validateValue rules (createContextForJsonValue [("Age", JsonValue.Float(39.0));]) "no rule"

    
    [<Fact>]
    member test.``Negation rule``() =
        let rules = parser <| """
        {
            "partitions": [],
            "defaultValue": "minor",
            "rules": [
                {
                    "Matcher": {
                        "Age": {
                            "$not": { "$lt": 21}
                        }
                    },
                    "Type": "SingleVariant",
                    "Value": "adult"
                }
            ]
        }"""
        
        validateValue rules (createContextForJsonValue [("Age", JsonValue.Float(22.0));]) "adult"
        validateValue rules (createContextForJsonValue [("Age", JsonValue.Float(21.0));]) "adult"
        validateValue rules (createContextForJsonValue [("Age", JsonValue.Float(20.0));]) "minor"

        

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

        (fun () -> (parser <| invalidJPad).Invoke (createContext [("fruit", "apple");("device.Version", "1.0")]) |> ignore) |> should throw typeof<ArgumentException>
        
    [<Theory>]
    [<InlineData(12.0, "false")>]
    [<InlineData(10.0, "true")>]
    [<InlineData(22.0, "false")>]
    [<InlineData(20.0, "true")>]
    [<InlineData(32.0, "false")>]
    [<InlineData(30.0, "true")>]
    member test.``In array rule``(age: double, expected: string) =
        let rules = parser <| """
        {
            "partitions": [],
            "defaultValue": "false",
            "rules": [
                {
                    "Matcher": {
                        "Age": {
                            "$in": [10,20,30]
                        }
                    },
                    "Type": "SingleVariant",
                    "Value": "true"
                }
            ]
        }"""
        
        validateValue rules (createContextForJsonValue [("Age", JsonValue.Float(age));]) expected

    [<Theory>]
    [<InlineData(25, 70, "true")>]
    [<InlineData(25, 80, "false")>]
    [<InlineData(22, 70, "true")>]
    [<InlineData(22, 80, "true")>]
    member test.``Rule with 'or' conjunction``(age: decimal, weight: decimal, expected: string) =
        let rules = parser <| """
        {
            "partitions": [],
            "defaultValue": "false",
            "rules": [
                {
                    "Matcher": {"$or": {"Age": {"$gt" : 20, "$lt" : 23 } , "Weight": {"$lt":80}} },
                    "Type": "SingleVariant",
                    "Value": "true"
                }
            ]
        }"""
        
        validateValue rules (createContextForJsonValue [("Age", JsonValue.Number(age));("Weight", JsonValue.Number(weight))]) expected

    
    [<Theory>]
    [<ClassData(typeof<ContainsOperatorData>)>]
    member test.``Rule with 'contains' operator``(expression: string, contextMap: IEnumerable<KeyValuePair<string,JsonValue>>, expected: bool) =
        let rules = parser <| (expression |> sprintf """
        {
            "partitions": [],
            "defaultValue": "false",
            "rules": [
                {
                    "Matcher": %s,
                    "Type": "SingleVariant",
                    "Value": "true"
                }
            ]
        }""")
        
        let contextMapAdapted = contextMap |> Seq.map(fun kv -> (kv.Key, kv.Value))
        validateValue rules (createContextForJsonValue contextMapAdapted) (expected.ToString().ToLower())


    [<Theory>]
    [<InlineData("""{"Country": {"$startsWith": "united" }}""", "United Stated", true)>]
    [<InlineData("""{"Country": {"$startsWith": "united" }}""", "United Kingdom", true)>]
    [<InlineData("""{"Country": {"$startsWith": "united" }}""", "Russia", false)>]
    [<InlineData("""{"Country": {"$endsWith": "land" }}""", "Finland", true)>]
    [<InlineData("""{"Country": {"$endsWith": "land" }}""", "EnglaND", true)>]
    [<InlineData("""{"Country": {"$endsWith": "land" }}""", "Norway", false)>]
    member test.``String operations with string value``(expression, value, expected) =
        let rules = parser <| (expression |> sprintf """
        {
            "partitions": [],
            "defaultValue": "false",
            "rules": [
                {
                    "Matcher": %s,
                    "Type": "SingleVariant",
                    "Value": "true"
                }
            ]
        }""")

        validateValue rules (createContext [("Country", value);]) (expected.ToString().ToLower())


    [<Theory>]
    [<InlineData("""{"Birthday": {"$withinTime": "10d"}}""", -20.0, false)>]
    [<InlineData("""{"Birthday": {"$withinTime": "10d"}}""", -5.0, true)>]
    member test.``DateCompare using withinTime with days``(expression, value, expected) =
        let rules = parser <| (expression |> sprintf """
        {
            "partitions": [],
            "defaultValue": "false",
            "rules": [
                {
                    "Matcher": %s,
                    "Type": "SingleVariant",
                    "Value": "true"
                }
            ]
        }""")
        
        let context = (createContextForJsonValue [("Birthday", JsonValue.String(DateTime.UtcNow.AddDays(value).ToString()));("system.time_utc", JsonValue.String(DateTime.UtcNow.ToString()));])
        validateValue rules context (expected.ToString().ToLower())
        
    [<Fact>]
    member test.``DateCompare using withinTime with days and missing values in context``() =
        let rules = parser <| """
        {
            "partitions": [],
            "defaultValue": "false",
            "rules": [
                {
                    "Matcher": {"Birthday": {"$withinTime": "10d"}},
                    "Type": "SingleVariant",
                    "Value": "true"
                }
            ]
        }"""
        
        let context1 = (createContextForJsonValue [])
        let context2 = (createContextForJsonValue [("Birthday", JsonValue.Null);])
        validateValue rules context1 "false"
        validateValue rules context2 "false"

    [<Theory>]
    [<InlineData("""{"Birthday": {"$withinTime": "10h"}}""", -20.0, false)>]
    [<InlineData("""{"Birthday": {"$withinTime": "10h"}}""", -5.0, true)>]
    member test.``DateCompare using withinTime with hours``(expression, value, expected) =
        let rules = parser <| (expression |> sprintf """
        {
            "partitions": [],
            "defaultValue": "false",
            "rules": [
                {
                    "Matcher": %s,
                    "Type": "SingleVariant",
                    "Value": "true"
                }
            ]
        }""")
        
        let context = (createContextForJsonValue [("Birthday", JsonValue.String(DateTime.UtcNow.AddHours(value).ToString()));("system.time_utc", JsonValue.String(DateTime.UtcNow.ToString()));])
        validateValue rules context (expected.ToString().ToLower())

    [<Theory>]
    [<InlineData("""{"Birthday": {"$withinTime": "10m"}}""", -20.0, false)>]
    [<InlineData("""{"Birthday": {"$withinTime": "10m"}}""", -5.0, true)>]
    member test.``DateCompare using withinTime with minutes``(expression, value, expected) =
        let rules = parser <| (expression |> sprintf """
        {
            "partitions": [],
            "defaultValue": "false",
            "rules": [
                {
                    "Matcher": %s,
                    "Type": "SingleVariant",
                    "Value": "true"
                }
            ]
        }""")

        let context = (createContextForJsonValue [("Birthday", JsonValue.String(DateTime.UtcNow.AddMinutes(value).ToString()));("system.time_utc", JsonValue.String(DateTime.UtcNow.ToString()));])
        validateValue rules context (expected.ToString().ToLower())

    [<Fact>]
    member test.``DateCompare with string comparer``() =
        let rules = parser <|  """
        {
            "partitions": [],
            "defaultValue": "false",
            "rules": [
                {
                    "Matcher": {"Birthday": {"$ge": "2014-12-20T13:14:19.790Z", "$compare": "date"}},
                    "Type": "SingleVariant",
                    "Value": "true"
                }
            ]
        }"""

        validateValue rules (createContextForJsonValue [("Birthday", JsonValue.String("2015-12-20T13:14:19.790Z"));] ) "true"
        validateValue rules (createContextForJsonValue [("Birthday", JsonValue.String("2013-12-20T13:14:19.790Z"));] ) "false"
        validateValue rules (createContextForJsonValue [("Birthday", JsonValue.Null);]) "false"
