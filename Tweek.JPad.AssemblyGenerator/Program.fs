open System
open Mono.Cecil
open Mono.Cecil.Cil
open Tweek.JPad


[<EntryPoint>]
let main argv =
    printfn "Generating .net core assembly"
    let translator = Translate.Translator("TweekRules", (Version(1,0)))
    let parser = JPadParser(ParserSettings(Sha1Provider(fun x->x)))
    let partitions = """
    {
    "partitions": [
        "user.Age",
        "user.Planet"
    ],
    "valueType": "string",
    "rules": {
        "1": {
            "*": [
                {
                    "Type": "SingleVariant",
                    "Matcher": {},
                    "Value": "one"
                }
            ]
        },
        "2": {
            "*": []
        },
        "3": {
            "Uranus": [
                {
                    "Type": "MultiVariant",
                    "Matcher": {},
                    "OwnerType": "user",
                    "ValueDistribution": {
                        "type": "weighted",
                        "args": [
                            {
                                "value": "Uranus3-1",
                                "weight": 70
                            },
                            {
                                "value": "Uranus3-2",
                                "weight": 30
                            }
                        ]
                    },
                    "Salt": "050a466e-7a65-5e14-aa26-420a0e83c12b"
                }
            ]
        },
        "*": {
            "*": [
                {
                    "Type": "SingleVariant",
                    "Matcher": {},
                    "Value": "Default"
                }
            ],
            "Mercury": []
        }
    },
    "defaultValue": "Hello"
}
    """
    let noPartitions = """
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
    }
    """
    let simpleAnd = """
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
            }
        ]
    }
    """
    // translator.appendMethodForKey("abc/partitions", parser.BuildAST partitions)
    translator.emitMethodForKey("abc_no_paritions", parser.BuildAST noPartitions)
    translator.writeAssemblyTodisk()

    printfn "Done generating assembly"

    0 // return an integer exit code
