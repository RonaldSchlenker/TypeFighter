﻿
#load "./visu/visu.fsx"

type Lit = { typeName: string; value: string }
type Exp =
    | ELit of Lit
    | EVar of string
    | EApp of Exp * Exp
    | EFun of string * Exp
    | ELet of string * Exp * Exp
type TyVar = int
type Ident = string
type Env = Map<Ident, TyVar>
type Annotated<'expr> =
    { annotated: 'expr
      tyvar: TyVar
      env: Env }
type TExp =
    | TELit of Lit
    | TEVar of Ident
    | TEApp of Annotated<TExp> * Annotated<TExp>
    | TEFun of Annotated<Ident> * Annotated<TExp>
    | TELet of Ident * Annotated<TExp> * Annotated<TExp>

module Env =
    let empty : Env = Map.empty
    let bind ident (tyvar: TyVar) (env: Env) : Env =
        env |> Map.change ident (fun _ -> Some tyvar)
    let resolve varName (env: Env) =
        match env |> Map.tryFind varName with
        | None -> failwith $"Variable '{varName}' is unbound."
        | Some t -> t

module Count =
    let create f =
        let mutable varCounter = 0
        fun () ->
            varCounter <- f varCounter 1
            varCounter
    let up () = create (+)
    let down () = create (-)

type System.Collections.Generic.List<'a> with
    member this.RemoveSafe(element: 'a) =
        if this.Remove element = false then failwith "element is not in list."

let annotate (env: Env) (exp: Exp) =
    let newvar = Count.up()

    let rec annotate (env: Env) (exp: Exp) =
        { tyvar = newvar()
          annotated =
              match exp with
              | ELit x ->
                  TELit x
              | EVar ident ->
                  TEVar ident
              | EApp (e1, e2) ->
                  TEApp (annotate env e1, annotate env e2)
              | EFun (ident, body) ->
                  let tyvarIdent = newvar()
                  let newEnv = env |> Env.bind ident tyvarIdent
                  let annotatedIdent = { annotated = ident; tyvar = tyvarIdent; env = env }
                  TEFun (annotatedIdent, annotate newEnv body)
              | ELet (ident, e, body) ->
                  let newEnv = env |> Env.bind ident (newvar())
                  TELet (ident, annotate env e, annotate newEnv body)
          env = env }
    annotate env exp

type Constraint =
    | CUnknown
    | CPoly of TyVar list
    | CClass of string * TyVar list
    | CBaseType of string
    | CFun of Constraint * Constraint
    
type Op =
    | MakeFunc
    | ApplyFunc
type NodeData =
    | Source of Constraint
    | Var of TyVar
    | Op of Op

type [<ReferenceEquality>] Node =
    { data: NodeData
      mutable constr: Constraint option
      incoming: ResizeArray<Edge>
      outgoing: ResizeArray<Edge> }
and [<ReferenceEquality>] Edge =
    { mutable constr: Constraint option
      fromNode: Node
      toNode: Node }
and Graph =
    { root: Node 
      nodes: ResizeArray<Node> }

module Graph =
    let addNode n (nodes: ResizeArray<Node>) = 
        let node =
            { data = n
              constr = match n with | Source c -> Some c | _ -> None
              incoming = ResizeArray()
              outgoing = ResizeArray() }
        do
            nodes.Add node
        node
    let getVarNodes (nodes: Node seq) =
        nodes |> Seq.choose (fun n -> 
            match n.data with 
            | Var var -> Some {| n = n; var = var |}
            | _ -> None)
    let getPolyNodes(nodes: Node seq) =
        nodes |> Seq.filter (fun n -> n.incoming.Count = 0)
    let connectNodes (a: Node) (b: Node) =
        let edge = { fromNode = a; toNode = b; constr = None }
        do
            a.outgoing.Add edge
            b.incoming.Add edge
        ()
    let addFuncNode n1 n2 ntarget graph =
        let nfunc = graph |> addNode (Op MakeFunc)
        do
            connectNodes n1 nfunc
            connectNodes n2 nfunc
            connectNodes nfunc ntarget
    let addApplyFuncNode nsource ntarget graph =
        let napp = graph |> addNode (Op ApplyFunc)
        do
            connectNodes nsource napp
            connectNodes napp ntarget
    let findNode (tyvar: TyVar) (nodes: Node seq) =
        nodes |> Seq.find (fun n ->
            match n.data with
            | Var v when v = tyvar -> true
            | _ -> false)        

let createConstraintGraph (exp: Annotated<TExp>) =
    let nodes = ResizeArray()
    let rec generateGraph (exp: Annotated<TExp>) =
        match exp.annotated with
        | TELit x ->
            let node = nodes |> Graph.addNode (Var exp.tyvar)
            let nsource = nodes |> Graph.addNode(Source (CBaseType x.typeName))
            do
                Graph.connectNodes nsource node
            node
        | TEVar ident ->
            let node = nodes |> Graph.addNode (Var exp.tyvar)
            let tyvarIdent = exp.env |> Env.resolve ident
            do
                Graph.connectNodes (nodes |> Graph.findNode tyvarIdent) node
            node
        | TEApp (e1, e2) ->
            let ne1 = generateGraph e1
            let ne2 = generateGraph e2
            let node = nodes |> Graph.addNode (Var exp.tyvar)
            do
                nodes |> Graph.addFuncNode ne2 node ne1
                nodes |> Graph.addApplyFuncNode ne1 node
            node
        | TEFun (ident, body) ->
            let nident = nodes |> Graph.addNode (Var ident.tyvar)
            let nbody = generateGraph body
            let node = nodes |> Graph.addNode (Var exp.tyvar)
            do
                nodes |> Graph.addFuncNode nident nbody node
            node
        | TELet (ident, e, body) ->
            let nident =
                let nodeData = body.env |> Env.resolve ident |> Var
                nodes |> Graph.addNode nodeData
            let ne = generateGraph e
            let nbody =  generateGraph body
            let node = nodes |> Graph.addNode (Var exp.tyvar)
            do
                Graph.connectNodes nbody node
                Graph.connectNodes ne nident
            node
    let rootNode = generateGraph exp
    { nodes = nodes
      root = rootNode }

let solve (graph: Graph) =
    let setEdgeConstraint constr (edges: Edge seq) =
        for e in edges do e.constr <- Some constr
    
    // Nodes with no incoming edges are forall constrained
    for x in Graph.getPolyNodes graph.nodes do
        let outgoingEdges = x.outgoing
        match x.data with
        | Source c -> setEdgeConstraint c outgoingEdges
        | Var tyvar ->
            let constr = CPoly [ tyvar ]
            x.constr <- Some constr
            setEdgeConstraint constr outgoingEdges
        | Op _ -> failwith "Operator node without incoming edges detected."

    // already visited and cannot do anything: ERROR
    ()


module GraphVisu =
    open Visu

    let showTyvar (ident: string) (tyvar: TyVar) =
        $"{{{ident} : {tyvar}}}"

    let showTyvarAndEnv exp =
        let envVars =
            match exp.env |> Map.toList with
            | [] -> "[ ]"
            | [(ident, tyvar)] -> $"[ {showTyvar ident tyvar} ]"
            | _ ->
                [ for x in exp.env do $"-  {showTyvar x.Key x.Value}" ]
                |> String.concat "\n"
                |> fun s -> $"[\n{s} ]"
        ($"var = {exp.tyvar}") + "\nenv = " + envVars

    let showConstraint (c: Constraint option) =
        match c with
        | None -> "()"
        | Some c -> string c

    let showAst (exp: Annotated<TExp>) =
        let rec flatten (node: TreeNode) =
            [
                yield node
                for c in node.children do
                    yield! flatten c
            ]
    
        let rec createNodes (exp: Annotated<TExp>) =
            match exp.annotated with
            | TELit x ->
                TreeNode.var $"Lit ({x.value}: {x.typeName})" (showTyvarAndEnv exp) []
            | TEVar ident ->
                let tyvar = Env.resolve ident exp.env                
                TreeNode.var $"Var {showTyvar ident tyvar}" (showTyvarAndEnv exp) []
            | TEApp (e1, e2) ->
                let child1 = createNodes e1
                let child2 = createNodes e2
            
                TreeNode.var $"App" (showTyvarAndEnv exp) [ child1; child2 ]
            | TEFun (ident, body) ->
                let child = createNodes body

                TreeNode.var
                    $"""fun {showTyvar ident.annotated ident.tyvar} -> {showTyvar "e" body.tyvar}"""
                    (showTyvarAndEnv exp)
                    [child]
            | TELet (ident, e, body) ->
                let child1 = createNodes e
                let child2 = createNodes body

                TreeNode.var
                    $"""let {ident} = {showTyvar "e1" e.tyvar} in {showTyvar "e2" body.tyvar}"""
                    (showTyvarAndEnv exp)
                    [ child1; child2 ]

        createNodes exp |> flatten |> writeTree

    let showConstraintGraph (graph: Graph) =
        let edges =
            [ for n in graph.nodes do
              yield! n.incoming
              yield! n.outgoing ]
            |> List.distinct
        let indexedNodes = graph.nodes |> Seq.indexed |> Seq.toList
        let nodesLookup = indexedNodes |> List.map (fun (a,b) -> b,a) |> readOnlyDict
        let jsLinks =
            edges
            |> List.map (fun edge ->
                { fromNode = nodesLookup.[edge.fromNode]
                  toNode = nodesLookup.[edge.toNode] })
        let jsNodes =
            [ for i,x in indexedNodes do
                let name, layout =
                    match x.data with
                    | Source _ -> "SOURCE", NodeTypes.op
                    | Var tyvar -> string tyvar, NodeTypes.var
                    | Op op -> string op, NodeTypes.op
                { key = i
                  name = name
                  desc = showConstraint x.constr
                  layout = layout }
            ]

        printfn "WRITING!!!"
        writeGraph jsNodes jsLinks Layouts.graph






[<AutoOpen>]
module Dsl =
    let knownBaseTypes =
        {| int = "Int"
           float = "Float"
           string = "String" |}
           
    let cint (x: int) = ELit { typeName = knownBaseTypes.int; value = string x }
    let cfloat (x: float) = ELit { typeName = knownBaseTypes.float; value = string x }
    let cstr (x: string) = ELit { typeName = knownBaseTypes.string; value = x }

//let env : Env = Map.ofList [
//    "libcall_add", tfun(tint, tfun(tint, tint))
//    ]
let env = Env.empty

let showAst exp = 
    annotate env exp
    |> GraphVisu.showAst
let showConstraintGraph exp =
    annotate env exp 
    |> createConstraintGraph
    |> GraphVisu.showConstraintGraph
let showSolved exp =
    let graph = annotate env exp |> createConstraintGraph 
    do
        solve graph
        GraphVisu.showConstraintGraph graph

let idExp = EFun("x", EVar "x")


// polymorphic let
(*
let id = fun x -> x in
    let f = id in
        let res1 = f 99 in
            let res2 = f "Hello World" in
                res2
*)
ELet("f", idExp,
    ELet("res1", EApp(EVar "f", cint 99),
        ELet("res2", EApp(EVar "f", cstr "HelloWorld"),
            EVar("res2")
)))
//|> annotate env |> createConstraintGraph
//|> showAst
//|> showConstraintGraph
|> showSolved

