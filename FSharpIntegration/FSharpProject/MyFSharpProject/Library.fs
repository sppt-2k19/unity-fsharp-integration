namespace MyFSharpProject
open UnityEngine

type Player() =
    inherit MonoBehaviour()
    
    member this.Start() = Debug.Log this.X
    
    member this.X = "F# in Unity"


type Friend() =
    inherit MonoBehaviour()
    
    member this.Start() = Debug.Log this.X
        
    member this.X = "F# working in Unity"
    
