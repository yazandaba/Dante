using System.Collections.Immutable;
using System.Diagnostics;
using Dante.Asserts;
using Dante.Extensions;
using Dante.Intrinsics;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.Extensions.Logging;
using Microsoft.Z3;
using Enumerable = Dante.Intrinsics.Enumerable;

namespace Dante.Generator;

internal sealed partial class ExpressionGenerator : OperationWalker<GenerationContext>
{
    private readonly SymbolEvaluationTable _owningBasicBlockSymbolEvalTable;
    private readonly FlowCaptureTable _owningControlFlowGraphFlowCaptureTable;
    private readonly ImmutableDictionary<string, Expr> _owningBasicBlockParameters;

    private static readonly ILogger<ExpressionGenerator> Logger = LoggerFactory.Create<ExpressionGenerator>();

    public ExpressionGenerator(
        SymbolEvaluationTable owningBasicBlockSymbolEvalTable,
        FlowCaptureTable owningControlFlowGraphFlowCaptureTable)
    {
        _owningBasicBlockSymbolEvalTable = owningBasicBlockSymbolEvalTable;
        _owningControlFlowGraphFlowCaptureTable = owningControlFlowGraphFlowCaptureTable;
        _owningBasicBlockParameters = ImmutableDictionary<string, Expr>.Empty;
    }

    public ExpressionGenerator(
        SymbolEvaluationTable owningBasicBlockSymbolEvalTable,
        FlowCaptureTable owningControlFlowGraphFlowCaptureTable,
        IReadOnlyList<Expr> owningBasicBlockParameters)
    {
        _owningBasicBlockSymbolEvalTable = owningBasicBlockSymbolEvalTable;
        _owningControlFlowGraphFlowCaptureTable = owningControlFlowGraphFlowCaptureTable;
        _owningBasicBlockParameters = owningBasicBlockParameters
            .ToImmutableDictionary(key => key.FuncDecl.Name.ToString(), value => value);
    }

    public override object? VisitExpressionStatement(IExpressionStatementOperation operation, GenerationContext context)
    {
        return operation.Operation.Accept(this, context);
    }

    #region Asignments

    public override Expr VisitSimpleAssignment(ISimpleAssignmentOperation operation, GenerationContext context)
    {
        var operand = operation.Value.Accept(this, context);
        GenerationAsserts.RequireValidExpression(operand, operation.Value);
        var operandExpr = (Expr)operand!;
        var targetRefOperation = operation.Target;
        if (targetRefOperation is IArrayElementReferenceOperation arrayElemRefOp)
        {
            var newArray = GenerateStore(arrayElemRefOp, context, operandExpr);
            UpdateReferencedSymbolValueInEvalTable(arrayElemRefOp, newArray, context);
            return newArray;
        }

        UpdateReferencedSymbolValueInEvalTable(targetRefOperation, operandExpr, context);
        return operandExpr;
    }

    public override Expr VisitCompoundAssignment(ICompoundAssignmentOperation operation, GenerationContext context)
    {
        var target = operation.Target.Accept(this, context);
        GenerationAsserts.RequireValidExpression(target, operation.Target);
        var operand = operation.Value.Accept(this, context);
        GenerationAsserts.RequireValidExpression(operand, operation.Value);
        var assignedExpr = GenerateArithmeticOrBitwiseExpression(operation, (Expr)target!, (Expr)operand!, context);
        UpdateReferencedSymbolValueInEvalTable(operation.Target, assignedExpr, context);
        return assignedExpr;
    }

    #endregion

    public override Expr VisitConversion(IConversionOperation operation, GenerationContext context)
    {
        var conversion = operation.Conversion;
        var convertedOperand = operation.Operand.Accept(this, context) as Expr;
        GenerationAsserts.RequireValidExpression(convertedOperand, operation);
        var convertedExpr = convertedOperand!;
        var conversionType = operation.Type!;
        if (conversion.IsNullable)
        {
            if (UnderlyingType.IsMaybe(convertedExpr)) return convertedOperand!;

            var maybe = MaybeIntrinsics.CreateOrGet(conversionType.AsNonNullableType());
            return MaybeIntrinsics.Some(maybe, convertedExpr);
        }

        if (conversion.IsNumeric)
        {
            if (conversionType.IsIntegral()) return Convertor.AsArithmeticExpression(convertedOperand!);

            if (conversionType.IsFloatingPoint()) return Convertor.AsFloatExpression(convertedOperand!, conversionType);
        }

        if (conversionType.IsEnumerable())
            if (operation.Operand.Type is IArrayTypeSymbol arrayType && convertedExpr is ArrayExpr arrayExpr)
                return Enumerable.CreateOrGet(arrayType, arrayExpr);

        return convertedOperand!;
    }

    #region Unary

    public override Expr VisitUnaryOperator(IUnaryOperation operation, GenerationContext context)
    {
        return GeneratePrefixOrPostfixUnaryExpression(operation, context);
    }

    public override Expr VisitIncrementOrDecrement(IIncrementOrDecrementOperation operation, GenerationContext context)
    {
        return GeneratePrefixOrPostfixUnaryExpression(operation, context);
    }

    private Expr GeneratePrefixOrPostfixUnaryExpression(IOperation operation, GenerationContext context)
    {
        OperationAsserts.RequirePrefixOrPostfixExpression(operation);
        var solverContext = context.SolverContext;
        var sortPool = context.SortPool;
        switch (operation)
        {
            case IUnaryOperation unaryOperation:
            {
                switch (unaryOperation.OperatorKind)
                {
                    case UnaryOperatorKind.True or UnaryOperatorKind.False
                        when unaryOperation.OperatorMethod is not null:
                    {
                        var overloadedUnaryBooleanFunc =
                            FunctionGenerator.DeclareFunctionFromMethod(unaryOperation.OperatorMethod, context);
                        overloadedUnaryBooleanFunc.Should().NotBeNull("overloaded true/false operator could not " +
                                                                      "be declared");
                        return overloadedUnaryBooleanFunc!.Apply();
                    }

                    case UnaryOperatorKind.Hat: throw new NotSupportedException("index operator '^' is not supported");

                    case UnaryOperatorKind.Plus:
                    {
                        var operandNode = (AST?)unaryOperation.Operand.Accept(this, context);
                        GenerationAsserts.RequireValidExpression(operandNode, unaryOperation);
                        var operandExpr = (Expr)operandNode!;
                        unaryOperation.Type.Should().NotBeNull("type of unary operand '{0}' could not be resolved",
                            unaryOperation.GetSyntaxNodeText());
                        return operandExpr;
                    }

                    case UnaryOperatorKind.Minus:
                    {
                        var operandNode = unaryOperation.Operand.Accept(this, context);
                        GenerationAsserts.RequireValidExpression(operandNode, unaryOperation);
                        var operandExpr = (Expr)operandNode!;
                        unaryOperation.Type.Should().NotBeNull("type of unary operand '{0}' could not be resolved",
                            unaryOperation.GetSyntaxNodeText());

                        if (unaryOperation.Type!.IsIntegral())
                        {
                            var arithmeticOperandExpr = Convertor.AsIntegerExpression(operandExpr);
                            return solverContext.MkMul(arithmeticOperandExpr, solverContext.MkInt(-1));
                        }

                        if (unaryOperation.Type!.IsFloatingPoint())
                        {
                            var fpOperandExpr =
                                Convertor.AsFloatExpression(operandExpr, unaryOperation.Type!, sortPool);
                            return solverContext.MkFPNeg(fpOperandExpr);
                        }

                        throw new NotSupportedException("arithmetic negate operator '-' can only be used" +
                                                        "with floating point or integral expressions");
                    }

                    case UnaryOperatorKind.Not:
                    {
                        var operandNode = (AST?)unaryOperation.Operand.Accept(this, context);
                        GenerationAsserts.RequireValidExpression<BoolExpr>(operandNode, unaryOperation);
                        var operandExpr = (BoolExpr)operandNode!;
                        unaryOperation.Type.Should().NotBeNull("type of unary operand '{0}' could not be resolved",
                            unaryOperation.GetSyntaxNodeText());
                        return !operandExpr;
                    }

                    case UnaryOperatorKind.BitwiseNegation when unaryOperation.OperatorMethod is null:
                    {
                        var operandNode = (AST?)unaryOperation.Operand.Accept(this, context);
                        GenerationAsserts.RequireValidExpression<IntExpr>(operandNode, unaryOperation);
                        var operandExpr = (IntExpr)operandNode!;
                        unaryOperation.Type.Should().NotBeNull("type of unary operand '{0}' could not be resolved",
                            unaryOperation.GetSyntaxNodeText());
                        return solverContext.MkNeg(operandExpr, unaryOperation.Operand);
                    }

                    case UnaryOperatorKind.BitwiseNegation:
                    {
                        var negateOverloadMethod = unaryOperation.OperatorMethod!;
                        var operatorFunc = FunctionGenerator.DeclareFunctionFromMethod(negateOverloadMethod, context);
                        operatorFunc.Should().NotBeNull("overloaded '~' operator could not " +
                                                        "be declared");
                        return operatorFunc!.Apply();
                    }

                    default: throw new UnreachableException("unreachable unary operator");
                }
            }

            case IIncrementOrDecrementOperation incOrDecOperation:
            {
                var targetNode = incOrDecOperation.Target.Accept(this, context);
                GenerationAsserts.RequireValidExpression(targetNode, incOrDecOperation);
                var targetLValue = (Expr)targetNode!;
                var newExpr = GenerateArithmeticOrBitwiseExpression(incOrDecOperation, targetLValue,
                    solverContext.MkInt(1), context);
                UpdateReferencedSymbolValueInEvalTable(incOrDecOperation.Target, newExpr, context);
                return incOrDecOperation.IsPostfix ? newExpr : targetLValue;
            }

            default:
                throw new NotSupportedException($"expression '{operation.GetSyntaxNodeText()}' is not supported " +
                                                $"as postfix nor prefix expression");
        }
    }

    #endregion Unary

    #region Binary

    public override Expr VisitBinaryOperator(IBinaryOperation operation, GenerationContext context)
    {
        var lhs = operation.LeftOperand.Accept(this, context);
        GenerationAsserts.RequireValidExpression(lhs, operation);
        var rhs = operation.RightOperand.Accept(this, context);
        GenerationAsserts.RequireValidExpression(rhs, operation);
        return GenerateBinaryExpression(operation, (Expr)lhs!, (Expr)rhs!, context);
    }

    private Expr GenerateBinaryExpression(IBinaryOperation binaryExpression, Expr lhs, Expr rhs,
        GenerationContext context)
    {
        if (binaryExpression.IsRelationalExpression())
            return GenerateRelationalExpression(binaryExpression, lhs, rhs, context);

        if (binaryExpression.IsLogicalExpression())
            return GenerateLogicalExpression(binaryExpression, lhs, rhs, context);

        if (binaryExpression.IsArithmeticOrBitwiseExpression())
            return GenerateArithmeticOrBitwiseExpression(binaryExpression, lhs, rhs, context);

        throw new UnreachableException();
    }


    #region LogicalAndRelational

    private BoolExpr GenerateLogicalExpression(
        IBinaryOperation binaryExpression,
        Expr lhs,
        Expr rhs,
        GenerationContext context)
    {
        OperationAsserts.RequiresLogicalExpression(binaryExpression);
        ExpressionAsserts.RequiresBooleanOperand(lhs, (CSharpSyntaxNode)binaryExpression.LeftOperand.Syntax);
        ExpressionAsserts.RequiresBooleanOperand(rhs, (CSharpSyntaxNode)binaryExpression.RightOperand.Syntax);
        return binaryExpression.OperatorKind switch
        {
            BinaryOperatorKind.And => context.SolverContext.MkAnd((BoolExpr)lhs, (BoolExpr)rhs),
            BinaryOperatorKind.Or => context.SolverContext.MkOr((BoolExpr)lhs, (BoolExpr)rhs),
            _ => throw new UnreachableException()
        };
    }

    private BoolExpr GenerateRelationalExpression(IBinaryOperation binaryExpression,
        Expr lhs,
        Expr rhs,
        GenerationContext context)
    {
        OperationAsserts.RequiresRelationalExpression(binaryExpression);
        var leftOprType = binaryExpression.LeftOperand.Type!;
        var rightOprType = binaryExpression.RightOperand.Type!;
        var binOperationType = leftOprType.IsFloatingPoint() ? leftOprType : rightOprType;
        //generate null equality expression (expr == null , expr != null)
        if (binaryExpression.IsNullableEqualityCheck())
        {
            if (binaryExpression.LeftOperand.IsNullLiteralOperation() && rhs is DatatypeExpr maybeRhs)
                return MaybeIntrinsics.HasValue(maybeRhs);

            if (binaryExpression.RightOperand.IsNullLiteralOperation() && lhs is DatatypeExpr maybeLhs)
                return MaybeIntrinsics.HasValue(maybeLhs);

            Logger.LogError("Equality expression '{expression}' was analyzed as being null equality comparison but " +
                            "could not be generated correctly, an abstract equality will get generated instead",
                binaryExpression.GetSyntaxNodeText());
            return binaryExpression.OperatorKind is BinaryOperatorKind.Equals
                ? context.SolverContext.MkEq(lhs, rhs)
                : !context.SolverContext.MkEq(lhs, rhs);
        }

        //generate string equality expression
        if (leftOprType.IsString() && rightOprType.IsString() && binaryExpression.IsEqualityExpression())
            return binaryExpression.OperatorKind is BinaryOperatorKind.Equals
                ? context.SolverContext.MkEq(lhs, rhs)
                : !context.SolverContext.MkEq(lhs, rhs);

        var sortPool = context.SortPool;
        switch (binOperationType.SpecialType)
        {
            case SpecialType.System_Int32 or SpecialType.System_Int64:
            {
                var leftExpr = Convertor.AsArithmeticExpression(lhs);
                var rightExpr = Convertor.AsArithmeticExpression(rhs);
                return GenerateNormalRelationalExpression(binaryExpression, leftExpr, rightExpr, context);
            }
            case SpecialType.System_Single:
            {
                var leftExpr = Convertor.AsFloatExpression(lhs, sortPool.SingleSort);
                var rightExpr = Convertor.AsFloatExpression(rhs, sortPool.SingleSort);
                return GenerateFloatingPointRelationalExpression(binaryExpression, leftExpr, rightExpr, context);
            }
            case SpecialType.System_Double:
            {
                var leftExpr = Convertor.AsFloatExpression(lhs, sortPool.DoubleSort);
                var rightExpr = Convertor.AsFloatExpression(rhs, sortPool.DoubleSort);
                return GenerateFloatingPointRelationalExpression(binaryExpression, leftExpr, rightExpr, context);
            }
            case SpecialType.System_Decimal:
            {
                var leftExpr = Convertor.AsFloatExpression(lhs, sortPool.DecimalSort);
                var rightExpr = Convertor.AsFloatExpression(rhs, sortPool.DecimalSort);
                return GenerateFloatingPointRelationalExpression(binaryExpression, leftExpr, rightExpr, context);
            }

            default:
                throw new NotSupportedException(
                    $"expression '{binaryExpression.GetSyntaxNodeText()}' is neither floating point nor" +
                    $"integeral expression but '{binOperationType.Name}' ");
        }
    }

    private BoolExpr GenerateNormalRelationalExpression(
        IBinaryOperation binaryExpression,
        ArithExpr lhs,
        ArithExpr rhs,
        GenerationContext context)
    {
        var solverContext = context.SolverContext;
        return binaryExpression.OperatorKind switch
        {
            BinaryOperatorKind.Equals => solverContext.MkEq(lhs, rhs),
            BinaryOperatorKind.NotEquals => solverContext.MkNot(solverContext.MkEq(lhs, rhs)),
            BinaryOperatorKind.LessThan => lhs < rhs,
            BinaryOperatorKind.LessThanOrEqual => lhs <= rhs,
            BinaryOperatorKind.GreaterThan => lhs > rhs,
            BinaryOperatorKind.GreaterThanOrEqual => lhs >= rhs,
            _ => throw new UnreachableException()
        };
    }

    private BoolExpr GenerateFloatingPointRelationalExpression(
        IBinaryOperation binaryExpression,
        FPExpr lhs,
        FPExpr rhs,
        GenerationContext context)
    {
        var solverContext = context.SolverContext;
        return binaryExpression.OperatorKind switch
        {
            BinaryOperatorKind.Equals => solverContext.MkFPEq(lhs, rhs),
            BinaryOperatorKind.NotEquals => solverContext.MkNot(solverContext.MkFPEq(lhs, rhs)),
            BinaryOperatorKind.LessThan => solverContext.MkFPLt(lhs, rhs),
            BinaryOperatorKind.LessThanOrEqual => solverContext.MkFPLEq(lhs, rhs),
            BinaryOperatorKind.GreaterThan => solverContext.MkFPGt(lhs, rhs),
            BinaryOperatorKind.GreaterThanOrEqual => solverContext.MkFPGEq(lhs, rhs),
            _ => throw new UnreachableException()
        };
    }

    #endregion LogicalAndRelational

    #region ArithmeticAndBitwise

    private Expr GenerateArithmeticOrBitwiseExpression(IOperation expression, Expr lhs, Expr rhs,
        GenerationContext context)
    {
        return GenerateArithmeticOrBitwiseExpressionCore(expression, lhs, rhs, context);
    }

    private Expr GenerateArithmeticOrBitwiseExpressionCore(
        IOperation expression,
        Expr lhs,
        Expr rhs,
        GenerationContext context)
    {
        var sortPool = context.SortPool;
        var expressionType = expression.Type!;
        switch (expressionType.SpecialType)
        {
            case SpecialType.System_Int32 or SpecialType.System_Int64:
            {
                var rightExpr = Convertor.AsArithmeticExpression(rhs);
                var leftExpr = Convertor.AsArithmeticExpression(lhs);
                return Convertor.AsIntegerExpression(
                    GenerateDecimalArithmeticOrBitwiseExpression(expression, leftExpr, rightExpr, context)
                );
            }
            case SpecialType.System_Single:
            {
                var rightExpr = Convertor.AsFloatExpression(rhs, sortPool.SingleSort);
                var leftExpr = Convertor.AsFloatExpression(lhs, sortPool.SingleSort);
                return GenerateFloatingPointArithmeticExpression(expression, leftExpr, rightExpr, context);
            }
            case SpecialType.System_Double:
            {
                var rightExpr = Convertor.AsFloatExpression(rhs, sortPool.DoubleSort);
                var leftExpr = Convertor.AsFloatExpression(lhs, sortPool.DoubleSort);
                return GenerateFloatingPointArithmeticExpression(expression, leftExpr, rightExpr, context);
            }
            case SpecialType.System_Decimal:
            {
                var rightExpr = Convertor.AsFloatExpression(rhs, sortPool.DecimalSort);
                var leftExpr = Convertor.AsFloatExpression(lhs, sortPool.DecimalSort);
                return GenerateFloatingPointArithmeticExpression(expression, leftExpr, rightExpr, context);
            }
            case SpecialType.System_String:
            {
                var rightExpr = Convertor.AsStringExpression(rhs);
                var leftExpr = Convertor.AsStringExpression(lhs);
                return GenerateStringConcatExpression(expression, leftExpr, rightExpr, context);
            }
            default:
                throw new NotSupportedException(
                    $"expression '{expression.GetSyntaxNodeText()}' is neither floating point nor " +
                    $"integral expression but '{expressionType.Name}' ");
        }
    }

    private Expr GenerateDecimalArithmeticOrBitwiseExpression(
        IOperation operation,
        ArithExpr lhs,
        ArithExpr rhs,
        GenerationContext context)
    {
        var solverContext = context.SolverContext;
        var operationType = operation.Type!;
        var primitiveTypeSize = operationType.PrimitiveTypeSizeInBits();
        return operation switch
        {
            IIncrementOrDecrementOperation { Kind: OperationKind.Increment } or
                ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } => lhs + rhs,

            IIncrementOrDecrementOperation { Kind: OperationKind.Decrement } or
                ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Subtract } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Subtract } => lhs - rhs,

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Multiply } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Multiply } => lhs * rhs,

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Divide } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Divide } => lhs / rhs,

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Remainder } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Remainder } => solverContext.MkMod(
                    Convertor.AsIntegerExpression(lhs), Convertor.AsIntegerExpression(rhs)),

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.LeftShift } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.LeftShift } => solverContext.MkBVSHL(
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(lhs)),
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(rhs))),

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.UnsignedRightShift } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.UnsignedRightShift } => solverContext.MkBVLSHR(
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(lhs)),
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(rhs))),

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.RightShift } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.RightShift } => solverContext.MkBVASHR(
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(lhs)),
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(rhs))),

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.And } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.And } => solverContext.MkBVAND(
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(lhs)),
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(rhs))),

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Or } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Or } => solverContext.MkBVOR(
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(lhs)),
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(rhs))),

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.ExclusiveOr } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.ExclusiveOr } => solverContext.MkBVXOR(
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(lhs)),
                    solverContext.MkInt2BV(primitiveTypeSize, Convertor.AsIntegerExpression(rhs))),

            _ => throw new NotSupportedException($"arithmetic or logical operation '{operation}' is not supported")
        };
    }

    private FPExpr GenerateFloatingPointArithmeticExpression(
        IOperation operation,
        FPExpr lhs,
        FPExpr rhs,
        GenerationContext context)
    {
        var solverContext = context.SolverContext;
        var sortPool = context.SortPool;
        var node = (CSharpSyntaxNode)operation.Syntax;
        return operation switch
        {
            IIncrementOrDecrementOperation { Kind: OperationKind.Increment } or
                ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } => solverContext.MkFPAdd(
                    sortPool.IEEE754Rounding, lhs, rhs),

            IIncrementOrDecrementOperation { Kind: OperationKind.Decrement } or
                ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Subtract } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Subtract } => solverContext.MkFPSub(
                    sortPool.IEEE754Rounding, lhs, rhs),

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Multiply } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Multiply } => solverContext.MkFPMul(
                    sortPool.IEEE754Rounding, lhs, rhs),

            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Divide } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Divide } => solverContext.MkFPDiv(
                    sortPool.IEEE754Rounding, lhs, rhs),

            _ => throw new NotSupportedException(
                $"floating point operation {node.GetSyntaxNodeText()} is not supported")
        };
    }

    private SeqExpr GenerateStringConcatExpression(
        IOperation operation,
        SeqExpr lhs,
        SeqExpr rhs,
        GenerationContext context)
    {
        var solverContext = context.SolverContext;
        return operation switch
        {
            ICompoundAssignmentOperation { OperatorKind: BinaryOperatorKind.Add } or
                IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } => solverContext.MkConcat(lhs, rhs),

            _ => throw new NotSupportedException($"expression '{operation}' is not string operation")
        };
    }

    #endregion ArithmeticAndBitwise

    #endregion Binary

    #region TermsAndApplications

    public override Expr VisitFieldReference(IFieldReferenceOperation operation, GenerationContext context)
    {
        var field = operation.Field;
        var fieldFullName = field.ToDisplayString();
        var solverContext = context.SolverContext;
        if (context.GlobalEvaluationTable.TryFetch(field, out Expr? value)) return value;

        var fieldDecl = solverContext.MkConstDecl(fieldFullName, field.Type.AsSort());
        var fieldRefExpr = fieldDecl.Apply();
        context.GlobalEvaluationTable.AddThenBind(field, fieldDecl, fieldRefExpr);
        return fieldRefExpr;
    }

    public override FuncDecl VisitMethodReference(IMethodReferenceOperation operation, GenerationContext context)
    {
        var method = operation.Method;
        if (context.GlobalEvaluationTable.TryFetch(method, out FuncDecl? funcDecl)) return funcDecl;

        funcDecl = FunctionGenerator.DeclareFunctionFromMethod(method, context);
        return funcDecl!;
    }

    public override Expr VisitPropertyReference(IPropertyReferenceOperation operation, GenerationContext context)
    {
        var property = operation.Property;
        var propertyFullName = property.ToDisplayString();
        var propertyFuncSort = property.Type.AsSort();
        if (operation.IsArrayLength()) return context.RecursionDepth;

        //if property getter was not auto generated by the compiler aka auto property, then we should invoke the getter
        if (property.WasSourceDeclared())
        {
            var propertyGetter = property.GetMethod;
            if (propertyGetter is not null)
            {
                var fetched = context.GlobalEvaluationTable.TryFetch(propertyGetter, out Expr? propertyGetterCall);
                if (fetched) return propertyGetterCall!;

                var propertyGetterDecl = FunctionGenerator.DeclareFunctionFromMethod(propertyGetter, context);
                propertyGetterCall = propertyGetterDecl!.Apply();
                return propertyGetterCall;
            }
        }

        if (context.GlobalEvaluationTable.TryFetch(property, out Expr? propertyRefExpr)) return propertyRefExpr;

        var propertyFuncDecl = context.SolverContext.MkConstDecl(propertyFullName, propertyFuncSort);
        propertyRefExpr = propertyFuncDecl.Apply();
        return propertyRefExpr;
    }

    public override object VisitEventAssignment(IEventAssignmentOperation operation, GenerationContext argument)
    {
        throw new NotSupportedException("event assignment is not supported");
    }

    public override object VisitEventReference(IEventReferenceOperation operation, GenerationContext argument)
    {
        throw new NotSupportedException("referencing assignment is not supported");
    }

    public override object VisitRaiseEvent(IRaiseEventOperation operation, GenerationContext argument)
    {
        throw new NotSupportedException("raising event is not supported");
    }

    public override Expr? VisitInvocation(IInvocationOperation operation, GenerationContext context)
    {
        var invokedMethod = operation.TargetMethod;
        var returnType = invokedMethod.ReturnType;
        var semantics = context.Compilation.GetSemanticModel(operation.Syntax.SyntaxTree);
        if (returnType.IsVoid())
        {
            var invocationSyntaxText = operation.Syntax.AsCSharpSyntaxNode().GetSyntaxNodeText();
            Logger.LogWarning(
                "skipping generation of invocation '{invocationText}' because it return type is void",
                invocationSyntaxText);
            return default;
        }

        if (operation.IsStringMethodCall(semantics)) return StringIntrinsics.AsStringMethodCall(operation, this);

        if (operation.IsHasValueNullableCall())
        {
            var genInstance = operation.Instance!.Accept(this, context) as MaybeExpr;
            genInstance.Should().BeOfType<MaybeExpr>("calling instance to 'System.Nullable<T>.HasValue' is not" +
                                                     "a maybe expression");
            return MaybeIntrinsics.HasValue(genInstance!);
        }

        if (operation.IsEnumerableCall(semantics)) return GenerateLinq(operation, context, semantics);

        var funcSort = invokedMethod.ReturnType.AsSort();
        var funcArgumentsSorts = operation
            .Arguments
            .Select(argument => argument.Value.Type!.AsSort())
            .ToArray();

        var funcArguments = operation
            .Arguments
            .Select(argument =>
            {
                var argValue = argument.Value.Accept(this, context);
                return argValue switch
                {
                    FuncDecl => throw new NotSupportedException("high order functions are not supported"),
                    Expr expr => expr,
                    _ => throw new NotSupportedException($"unsupported expression: {argument.Syntax}")
                };
            })
            .ToArray();


        var methodFullName = operation.TargetMethod.ToDisplayString();
        if (_owningBasicBlockSymbolEvalTable.TryFetch(invokedMethod, out FuncDecl? func))
            return func.Apply(funcArguments);
        //TODO handle out or ref arguments if invoked method was declared as part of source
        func = context.SolverContext.MkFuncDecl(methodFullName, funcArgumentsSorts, funcSort);
        context.GlobalEvaluationTable.TryAdd(invokedMethod, func);
        return func.Apply(funcArguments);
    }

    public override Expr VisitLiteral(ILiteralOperation operation, GenerationContext context)
    {
        var sortPool = context.SortPool;
        var solverContext = context.SolverContext;
        var type = operation.Type;
        var literalValOpt = operation.ConstantValue;
        literalValOpt.HasValue.Should().BeTrue("literal must be a const value");
        if (operation.IsNullableLiteral()) return operation.AsDefaultMaybeExpression();

        type.Should().NotBeNull($"type of literal '{((CSharpSyntaxNode)operation.Syntax).GetSyntaxNodeText()}' " +
                                $"could not be deduced");
        var literalVal = literalValOpt.Value!;
        return type!.SpecialType switch
        {
            SpecialType.System_SByte => solverContext.MkInt((sbyte)literalVal),
            SpecialType.System_Byte => solverContext.MkInt((byte)literalVal),
            SpecialType.System_Int16 => solverContext.MkInt((short)literalVal),
            SpecialType.System_UInt16 => solverContext.MkInt((ushort)literalVal),
            SpecialType.System_Int32 => solverContext.MkInt((int)literalVal),
            SpecialType.System_UInt32 => solverContext.MkInt((uint)literalVal),
            SpecialType.System_Int64 => solverContext.MkInt((long)literalVal),
            SpecialType.System_UInt64 => solverContext.MkInt((ulong)literalVal),
            SpecialType.System_Single => solverContext.MkFPNumeral((float)literalVal, sortPool.SingleSort),
            SpecialType.System_Double => solverContext.MkFPNumeral((double)literalVal, sortPool.DoubleSort),
            SpecialType.System_Decimal => solverContext.MkNumeral(literalVal.ToString(), sortPool.DecimalSort),
            SpecialType.System_Boolean when literalVal is true => solverContext.MkTrue(),
            SpecialType.System_Boolean when literalVal is false => solverContext.MkFalse(),
            SpecialType.System_Char or SpecialType.System_String => solverContext.MkString(literalVal.ToString()),
            _ => throw new NotSupportedException($"literal of type '{literalVal.GetType()}' is not supported")
        };
    }


    public override Expr VisitLocalReference(ILocalReferenceOperation operation, GenerationContext context)
    {
        var local = operation.Local;
        if (_owningBasicBlockParameters.TryGetValue(local.Name, out var basicBlockParameterSymbolExpr))
            return basicBlockParameterSymbolExpr;

        //variable being referenced after being checked it is not null,
        //Type of captured local reference operation is guaranteed to produce non nullable expression if and only if
        //referenced local was checked to be null in a strictly dominating basic block of operation basic block 
        if (local.Type.IsNullable() && !operation.Type!.IsNullable())
        {
            var referencedSymbolVal = GenerateParameterOrLocalReference(local);
            return UnderlyingType.IsMaybe(referencedSymbolVal)
                ? MaybeIntrinsics.Value((DatatypeExpr)referencedSymbolVal)
                : referencedSymbolVal;
        }

        return GenerateParameterOrLocalReference(local);
    }

    public override Expr VisitParameterReference(IParameterReferenceOperation operation, GenerationContext context)
    {
        var parameter = operation.Parameter;
        if (parameter.Type.IsNullable() && !operation.Type!.IsNullable())
        {
            var referencedSymbolVal = GenerateParameterOrLocalReference(parameter);
            return UnderlyingType.IsMaybe(referencedSymbolVal)
                ? MaybeIntrinsics.Value((DatatypeExpr)referencedSymbolVal)
                : referencedSymbolVal;
        }

        return GenerateParameterOrLocalReference(parameter);
    }

    private Expr GenerateParameterOrLocalReference(ISymbol symbol)
    {
        var fetched = _owningBasicBlockSymbolEvalTable.TryFetch(symbol, out Expr? symbolValue);
        fetched.Should().BeTrue($"referenced local or parameter '{symbol}' is not part of the evaluation" +
                                $"stack but is should be");
        return symbolValue!;
    }

    #endregion TermsAndApplications

    private void UpdateReferencedSymbolValueInEvalTable(IOperation operation, Expr newExpr, GenerationContext context)
    {
        switch (operation)
        {
            case ILocalReferenceOperation localRef:
                _owningBasicBlockSymbolEvalTable.Bind(localRef.Local, newExpr);
                break;
            case IParameterReferenceOperation paramRef:
                _owningBasicBlockSymbolEvalTable.Bind(paramRef.Parameter, newExpr);
                break;
            case IArrayElementReferenceOperation arrayElementRef:
            {
                try
                {
                    UpdateReferencedSymbolValueInEvalTable(arrayElementRef.ArrayReference, newExpr, context);
                }
                catch (NotSupportedException)
                {
                    Logger.LogWarning("indirect reference to array is not supported, code generation " +
                                      "will continue but references to array will ignore current change to array state.\n" +
                                      "we recommend splitting the indirect reference (eg: expression[index]) as:\n" +
                                      "new local variable: var newArray = expression \n" +
                                      "reference the element through the local: newArray[index]\n");
                }

                break;
            }
            case IPropertyReferenceOperation { Property.SetMethod: not null } propertyRef:
                throw new NotSupportedException(
                    $"property '{propertyRef.Property}' has custom setter, which is not supported");
            case IPropertyReferenceOperation { Property: { SetMethod: null, GetMethod: null } } propertyRef:
                context.GlobalEvaluationTable.Bind(propertyRef.Property, newExpr);
                break;
            case IPropertyReferenceOperation { Property.GetMethod: not null } propertyRef:
                context.GlobalEvaluationTable.Bind(propertyRef.Property, newExpr);
                break;
            case IMethodReferenceOperation:
                break;
            case IFlowCaptureReferenceOperation captureRef:
                UpdateReferencedSymbolValueInEvalTable(
                    _owningControlFlowGraphFlowCaptureTable.Captured(captureRef),
                    newExpr, context);
                break;
            default:
                throw new NotSupportedException(
                    $"cannot handle evaluation of referenced object '{operation.GetSyntaxNodeText()}'");
        }
    }
}