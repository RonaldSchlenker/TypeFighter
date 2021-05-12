
#load "visuBase.fsx"
open VisuBase
open TestBase


(*
    let x = 10.0
    map Numbers (number ->
        add number x)
*)

let env1 = env [ map; add; numbers ]

(Let "x" (Num 10.0)
(MapX (Var "Numbers") (Abs "number"
    (Appn (Var "add") [ Var "number"; Var "x" ] ))))
|> showSolvedAstWEnv env1
|> Test.isOfType "map numbers by add" env1 (seqOf numberTyp)




(*
    let x = { a = 5.0; b = "hello" }
    x.b
*)
let env2 = env [ ]

(Let "x" (Record [ ("a", Num 5.0); ("b", Str "hello") ])
(Prop "b" (Var "x")))
|> showSolvedAst env2
|> Test.isOfType "Get record property" env2 stringTyp




(*
    [ 1.0; 2.0; 3.0 ]   
*)
let env3 = env [ cons; emptyList ]

NewList [ Num 1.0; Num 2.0; ]
|> showSolvedAst env3
//|> showSolvedGraph env3
|> Test.isOfType "Num list" env3 (seqOf numberTyp)

NewList [ Num 1.0; Num 2.0; Str "xxx" ]
|> showSolvedAst env3
|> showSolvedGraph env3
|> Test.isError "Disjunct list element types" env3





(*
    [ 1.0 ] |> map (fun x -> tostring x)
*)
let env4 = env [ add; tostring; mapp; filterp; cons; emptyList ]

MapP (Abs "x" (App (Var "tostring") (Var "x")))
|> showSolvedAst env4
|> Test.isOfType "Lambda applied to MapP" env4 (seqOf %1 ^-> seqOf stringTyp)

(*
    x => tostring(x)
*)

(Abs "x" (App (Var "tostring") (Var "x")))
|> Test.isOfType "Lambda with anon type" env4 (%1 ^-> stringTyp)
//|> showSolvedAst env4





// Polymorphic let
let env5 = env []
(*
    let id = fun x -> x
    (id "Hello World", id 42.0)
*)

(Let "id" (Abs "x" (Var "x"))
(Tuple [ App (Var "id") (Str "Hello World"); App (Var "id") (Num 42.0) ])
)
//|> showSolvedAst env5
|> Test.isOfType "Polymorphic let" (env5) (stringTyp * numberTyp)


(App
(Abs "id" (Tuple [ App (Var "id") (Str "Hello World"); App (Var "id") (Num 42.0) ]))
(Abs "x" (Var "x")))
|> Test.isError "No polymorphic abs" env5
//|> showSolvedAst env5




let env6 = env [ add; mul; sub; div; tostring ]

let mul2 = App (Var "mul") (Num 2.0)
let add3 = App (Var "add") (Num 3.0)
let tostr = Var "tostring"

(App tostr
(App mul2
(App add3
(Num 10.0))))
|> Test.isOfType "Many abs / currying" env6 stringTyp
//|> showSolvedAst env6




let env7 = env [ ]

(Let "x" (Num 7.0)
(Let "x" (Str "Hello")
(Var "x")))
|> Test.isOfType "Shadowing" env7 stringTyp
//|> showSolvedAst env7




// TODO: Generic records / instanciation of generic records
let env8 = env [ ]
(Let "id" (Abs "x" (Record [ "whatever", Var "x" ] ))
(Tuple [ App (Var "id") (Str "Hello World"); App (Var "id") (Num 42.0) ])
)
//|> showSolvedAst env8
|> Test.isOfType "Generic records / instanciation of generic records" (env8) (stringTyp * numberTyp)




// TODO: unused abs field (Constraints sind nicht ganz korrekt bei Fun__)
let env9 = env [ ]
App (Abs "__" (Var "__")) (Num 0.0)
|> showSolvedGraph env9
|> showSolvedAst env9
|> Test.isOfType "unused abs field" env9 (numberTyp)


(*
    fun f -> f 42.0
*)
let env10 = env [ ]
(Abs "f" (App (Var "f") (Num 42.0)))
|> showSolvedAst env10
|> showSolvedGraph env10
//|> Test.isOfType "infer function type for lambda" env10 ((numberTyp ^-> %1) ^-> %1)

//fun f -> f 42.0




