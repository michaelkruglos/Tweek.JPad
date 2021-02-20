module JsonHelper

open FSharpUtils.Newtonsoft
open Newtonsoft.Json
open Newtonsoft.Json.Linq

let rec toNewtonsoft j =
    match j with
    | String str -> JValue.CreateString(str) :> JToken
    | Number n -> JValue.op_Implicit(n)
    | Float f -> JValue.op_Implicit(f)
    | Boolean b -> JValue.op_Implicit(b)
    | Null -> JValue.CreateNull() :> JToken
    | Array elements ->
        let result = JArray()
        Seq.iter(fun item -> result.Add(toNewtonsoft(item))) elements
        result :> JToken
    | Record properties ->
        Seq.fold (fun (state: JObject) prop ->
            state.Add(fst prop, snd prop |> toNewtonsoft)
            state) (JObject()) properties
        :> JToken

let stringify (j:JsonValue) (indented:bool) : string = toNewtonsoft(j).ToString(if indented then Formatting.Indented else Formatting.None)
    

