namespace Kanren.Data.Tests

open FSharp.Quotations;
open System
open Swensen.Unquote
open Kanren.Data
open Kanren.Data.Compiler
open NUnit.Framework

module QuotationTests =
    let newParserInfo (expr: Expr) =
        let varset = QuotationParser.getVars VarSet.init expr
        let testSourceInfo =
                match (QuotationParser.getSourceInfo expr) with
                | Some sourceInfo -> sourceInfo
                | None -> { SourceInfo.File = "..."; StartLine = 0; EndLine= 0; StartCol = 0; EndCol = 0 }
        ParserInfo.init varset testSourceInfo

    type Union =
    | Case1 of x: int * y: int
    | Case2 of a: int * b: int
    | Case3 of c: int * d: int

    [<ReflectedDefinitionAttribute>]
    let testVarName info var varName = info.varset.[var].Name = varName

    let compileExpr expr =
        let ((args, goal), info) = State.run (QuotationParser.translateExpr expr) (newParserInfo expr)
        let (goal', varset) = Quantification.implicitlyQuantifyGoal args info.varset goal
        ((args, goal'), { info with varset = varset })

    [<Test>]
    let simple () : unit =
        let expr = <@ fun (x, y) -> x = 4 && y = 2 @>
        let ((args, goal), info) = compileExpr expr

        test <@ info.errors = [] @>
        test <@
                match args with
                | [arg1; arg2] ->
                    testVarName info arg1 "x" && testVarName info arg2 "y"
                | _ ->
                    false
                @>

        match goal.Goal with
        | Conj([{ Goal = Unify(var1, Constructor(Constant(IntValue(arg1), _), [], _, _), _, _) };
                { Goal = Unify(var2, Constructor(Constant(IntValue(arg2), _), [], _, _), _, _) }]) ->
            test <@ testVarName info var1 "x" @>
            test <@ testVarName info var2 "y" @>
            test <@ arg1 = 4L @>
            test <@ arg2 = 2L @>
        | _ -> raise(Exception($"invalid goal {goal.Goal}"))

    [<Test>]
    let singleArg () : unit =
        let expr = <@ fun x -> x = 4  @>
        let ((args, goal), info) = compileExpr expr

        test <@ info.errors = [] @>
        test <@
                match args with
                | [arg1] ->
                   testVarName info arg1 "x"
                | _ ->
                    false
                @>
        match goal.Goal with
        | Unify(var1, Constructor(Constant(IntValue(arg1), _), [], _, _), _, _) ->
            test <@ testVarName info var1 "x" @>
            test <@ arg1 = 4L @>
        | _ -> raise(Exception($"invalid goal {goal.Goal}"))

    [<Test>]
    let matchCase () : unit =
        let expr = <@ fun (x, y) -> match x with
                                    | Case1(a, b) -> a = b && y = "Case1"
                                    | Case2(c, d) -> c = d && y = "Case2"
                                    | Case3(e, f) -> e = f && y = "Case3" @>
        let ((args, goal), info) = compileExpr expr
        test <@ info.errors = [] @>

        match goal.Goal with
        | Disj([disjunct1; disjunct2; disjunct3]) ->
            let checkDisjunct disjunct =
                match disjunct.Goal with
                | Conj([
                        { Goal = Unify(lhs, Constructor(UnionCase(case), [_; _], _, _), _, _) };
                        { Goal = Unify(lhsd, Constructor( UnionCase(cased), [_; _], _, _), _, _) };
                        { Goal = Unify(lhst, Var(rhst, _), _, _) };
                        { Goal = Unify(lhs2, Constructor(Constant(StringValue(constant), _), [], _, _), _, _) }]) ->
                    test <@ constant = case.Name @>
                | _ ->
                    raise(Exception($"unexpected disjunct {goal.Goal}"))
            do checkDisjunct disjunct1
            do checkDisjunct disjunct2
            do checkDisjunct disjunct3
        | _ -> raise(Exception($"unexpected goal {goal.Goal}"))

    [<Test>]
    let deconstructTuple () : unit =
        let expr = <@ fun (x, y) ->
                            x = 1
                            && let (a, b) = y in a = b
                        @>
        let ((args, goal), info) = compileExpr expr
        test <@ info.errors = [] @>
        test <@
                match args with
                | [arg1; arg2] ->
                    testVarName info arg1 "x" && testVarName info arg2 "y"
                | _ ->
                    false
            @>
        match goal.Goal with
        | Conj([{ Goal = Unify(var1, Constructor(Constant(IntValue(arg1), _), [], _, _), _, _) };
                { Goal = Unify(var2, Constructor(Tuple 2, [var3; var4], _, _), _, _) };
                { Goal = Unify(var5, Var(var6, _), _, _) }]) ->
            test <@ testVarName info var1 "x" @>
            test <@ arg1 = 1L @>
            test <@ testVarName info var2 "y" @>
            test <@ testVarName info var3 "a" @>
            test <@ testVarName info var4 "b" @>
            test <@ var3 = var5 @>
            test <@ var4 = var6 @>
        | _ -> raise(Exception($"unexpected goal {goal.Goal}"))

    [<Test>]
    let deconstructTuple2 () : unit =
            let expr = <@ fun (
                                (a, (e, { Modes = m; Determinism = d }: RelationMode), c),
                                x) ->
                                                x = e
                                                && a = c
                                                && m = []
                                                && d = Determinism.Det
                            @>
            let ((args, goal), info) = compileExpr expr
            test <@ info.errors = [] @>
            match goal.Goal with
            | Conj([{ Goal = Unify(arg1, Constructor(Tuple 3, [arga; argeModes1; argc], _, _), _, _) };
                    { Goal = Unify(argeModes2, Constructor(Tuple 2, [arge; argModes1], _, _), _, _) };
                    { Goal = Unify(argModes2, Constructor(Record(relationModeType), [argm; argd], _, _), _, _) };
                    { Goal = Unify(argx2, Var(arge2, _), _, _) };
                    { Goal = Unify(arga2, Var(argc2, _), _, _) };
                    { Goal = Unify(argm2, Constructor(UnionCase(listEmptyCase), [], _, _), _, _) };
                    { Goal = Unify(argd2, Constructor(UnionCase(determinismDetCase), [], _, _), _, _) }]) ->
                test <@ testVarName info arga "a" @>
                test <@ arg1 = args.[0] @>
                test <@ testVarName info argc "c" @>
                test <@ argeModes1 = argeModes2 @>
                test <@ argModes1 = argModes2 @>
                test <@ testVarName info arge "e" @>
                test <@ testVarName info argd "d" @>
                test <@ testVarName info argm "m" @>
                test <@ testVarName info argx2 "x" @>
                test <@ arge2 = arge @>
                test <@ arga2 = arga @>
                test <@ argm2 = argm @>
                test <@ argc2 = argc @>
                test <@ argd2 = argd @>
                test <@ listEmptyCase.Name = "Empty" @>
                test <@ relationModeType.Name = "RelationMode" @>
                test <@ determinismDetCase.Name = "Det" @>
            | _ -> raise(Exception($"unexpected goal {goal.Goal}"))

    [<Test>]
    let exists () : unit =
        let expr = <@ fun (x, y) -> kanren.exists(fun z -> x = 4 && y = 2 && z = 3) @>
        let ((args, goal), info) = compileExpr expr
        test <@ info.errors = [] @>
        match goal.Goal with
        | Conj([{ Goal = Unify(var1, Constructor(Constant(IntValue(arg1), _), [], _, _), _, _) };
                { Goal = Unify(var2, Constructor(Constant(IntValue(arg2), _), [], _, _), _, _) };
                { Goal = Unify(var3, Constructor(Constant(IntValue(arg3), _), [], _, _), _, _) }]) ->
            test <@ testVarName info var1 "x" @>
            test <@ testVarName info var2 "y" @>
            test <@ testVarName info var3 "z" @>
            test <@ arg1 = 4L @>
            test <@ arg2 = 2L @>
            test <@ arg3 = 3L @>
        | _ -> raise(Exception($"unexpected goal {goal.Goal}"))

    [<Test>]
    let existsTuple () : unit =
        let expr = <@ fun (x, y) -> kanren.exists(fun (z1, z2) -> x = 4 && y = 2 && z1 = 6 && z2 = 7) @>
        let ((args, goal), info) = compileExpr expr
        test <@ info.errors = [] @>
        match goal.Goal with
        | Conj([{ Goal = Unify(var1, Constructor(Constant(IntValue(arg1), _), [], _, _), _, _) };
                { Goal = Unify(var2, Constructor(Constant(IntValue(arg2), _), [], _, _), _, _) };
                { Goal = Unify(var3, Constructor(Constant(IntValue(arg3), _), [], _, _), _, _) };
                { Goal = Unify(var4, Constructor(Constant(IntValue(arg4), _), [], _, _), _, _) }]) ->
            test <@ testVarName info var1 "x" @>
            test <@ testVarName info var2 "y" @>
            test <@ testVarName info var3 "z1" @>
            test <@ testVarName info var4 "z2" @>
            test <@ arg1 = 4L @>
            test <@ arg2 = 2L @>
            test <@ arg3 = 6L @>
            test <@ arg4 = 7L @>
        | _ -> raise(Exception($"unexpected goal {goal.Goal}"))


