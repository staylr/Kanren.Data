module internal Casgliad.Data.Compiler.DisjunctiveNormalForm

type IsLinear =
    | NonLinear
    | LeftLinear
    | RightLinear
    | MultiLinear

type IsRecursive =
    | Recursive
    | NotRecursive

type IsNegated =
    | Negated
    | Positive

type AtomCallee =
    | Relation of RelationProcId * IsRecursive * IsNegated * input: RelationProcId option
    | Input

type AtomExpr =
    | True
    | RelationCall of
        callee: AtomCallee *
        args: VarId list *
        selectProjectCondition: Goal *
        selectProjectOutputs: VarId list
// | Aggregate

type Atom = { Atom: AtomExpr; Info: GoalInfo }

type RuleDefinition = Atom list

type RulesRelation =
    { RelationId: RelationProcId
      OriginalRelationProcId: RelationProcId Option
      SourceInfo: SourceInfo
      Args: VarId list
      Modes: (InstE * BoundInstE) list
      Determinism: Casgliad.Data.Determinism
      ExitRules: RuleDefinition list
      RecursiveRules: RuleDefinition list }

let replaceGoal goal goal' = { goal with Goal = goal' }

type DnfInfo =
    { NewRelations: ResizeArray<RulesRelation>
      mutable Counter: int
      RelationProcId: RelationProcId
      VarSet: VarSet }

let rec goalIsAtomicOrNonRelational goal =
    goalIsAtomic goal
    || not (containsRelationCall goal)
    || match goal.Goal with
       | Not negGoal when goalIsAtomic negGoal -> true
       | Not ({ Goal = Conjunction ({ Goal = Call _ } :: conjGoals) }) when
           List.forall (fun g -> not (containsRelationCall g)) conjGoals
           ->
           true
       | Scope (_, scopeGoal) -> goalIsAtomic scopeGoal
       | _ -> false

let rulesRelationOfGoal (origName: RelationProcId) (name: RelationId) (goal: Goal) (args: VarId list) (instMap0: InstMap) (varSet: VarSet) : (RulesRelation * Atom) =
    let instMap =
        instMap0.applyInstMapDelta (goal.Info.InstMapDelta)

    let getArgMode arg =
        let inst0 = instMap0.lookupVar (arg)
        let inst = instMap.lookupVar (arg)

        match inst with
        | Free -> invalidOp $"unexpected unbound argument {arg}"
        | Bound boundInst -> (inst0, boundInst)

    let modes = List.map getArgMode args

    let relationProcId = (name, invalidProcId)
    let newRelation =
        { RulesRelation.RelationId = relationProcId; OriginalRelationProcId = Some origName; Args = args; Modes = modes; Determinism = goal.Info.Determinism; SourceInfo = goal.Info.SourceInfo;
            ExitRules = []; RecursiveRules = [] } 

    let callee = Relation (relationProcId, IsRecursive.NotRecursive, IsNegated.Positive, None)
    let selectProjectCondition = succeedGoal
    let selectProjectOutputs = []
    let goal =
        { Atom = RelationCall (callee, args, selectProjectCondition, selectProjectOutputs);
            Info = goal.Info }

    (newRelation, goal)

let createRelation (dnfInfo: DnfInfo) instMap goal =
    let newRelationName =
        { ModuleName = (fst dnfInfo.RelationProcId).ModuleName
          RelationName = TransformedRelation(dnfInfo.RelationProcId, DisjunctiveNormalFormSubgoal dnfInfo.Counter)
          }

    do dnfInfo.Counter <- dnfInfo.Counter + 1

    let (newRelation, goal') =
        rulesRelationOfGoal dnfInfo.RelationProcId newRelationName goal (TagSet.toList (goal.Info.NonLocals)) instMap (dnfInfo.VarSet)

    dnfInfo.NewRelations.Add (newRelation)
    goal'

let rec dnfProcessGoal dnfInfo instMap goal =
    if (goalIsAtomicOrNonRelational goal) then
        goal
    else
        match goal.Goal with
        | Conjunction conjuncts ->
            dnfProcessConjunction dnfInfo instMap conjuncts
            |> Conjunction
            |> replaceGoal goal
        | Disjunction disjuncts ->
            disjuncts
            |> List.map (dnfProcessGoal dnfInfo instMap)
            |> Disjunction
            |> replaceGoal goal
        | Not negGoal ->
            dnfProcessGoal dnfInfo instMap negGoal
            |> Not
            |> replaceGoal goal
        | Scope (reason, scopeGoal) ->
            let scopeGoal' = dnfProcessGoal dnfInfo instMap scopeGoal
            Scope (reason, scopeGoal') |> replaceGoal goal
        | IfThenElse (condGoal, thenGoal, elseGoal) ->
            let negatedCond =
                { Goal = Not (condGoal)
                  Info =
                      { condGoal.Info with
                            Determinism = negationDeterminismThrow condGoal.Info.Determinism } }

            let disjunction =
                Disjunction (
                    [ conjoinGoals [ condGoal; thenGoal ] goal.Info
                      conjoinGoals [ negatedCond; elseGoal ] goal.Info ]
                )

            dnfProcessGoal dnfInfo instMap { Goal = disjunction; Info = goal.Info }
        | Switch _ -> failwith "unexpected switch"
        | Call _
        | FSharpCall _
        | Unify _ -> failwith "unexpected atomic goal"

and dnfProcessConjunction (dnfInfo: DnfInfo) instMap conjuncts =
    conjuncts
    //|> List.mapFold
    //    (fun (instMap': InstMap) goal ->
    //        let goal' = stripTopLevelScopes goal

    //        let finalGoal =
    //            if (goalIsAtomicOrNonRelational goal) then
    //                goal'
    //            else
    //                let goal'' = dnfProcessGoal dnfInfo instMap' goal'

    //                match goal''.Goal with
    //                | Not negGoal ->
    //                    { Goal = Not (createRelation dnfInfo instMap negGoal)
    //                      Info = goal.Info }
    //                | _ -> createRelation dnfInfo instMap goal''

    //        (finalGoal, instMap'.applyInstMapDelta (goal.Info.InstMapDelta)))
    //    instMap
    //|> fst

