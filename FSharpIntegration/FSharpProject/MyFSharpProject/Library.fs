namespace MyFSharpProject
open UnityEngine

type Player() =
    inherit MonoBehaviour()
    
    member this.Start() = Debug.Log this.X
    
    member this.X = "It actually works :DD"


type Friend() =
    inherit MonoBehaviour()
    
    member this.Start() = Debug.Log this.X
        
    member this.X = "F# right here"
    
