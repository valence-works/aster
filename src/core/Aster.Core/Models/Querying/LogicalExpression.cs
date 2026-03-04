namespace Aster.Core.Models.Querying;

/// <summary>
/// Logical operator for combining filter expressions.
/// </summary>
public enum LogicalOperator
{
    /// <summary>All operands must match.</summary>
    And,

    /// <summary>At least one operand must match.</summary>
    Or,

    /// <summary>The single operand must NOT match. Expects exactly one operand.</summary>
    Not,
}

/// <summary>
/// Combines multiple <see cref="FilterExpression"/> instances with a logical operator (AND / OR / NOT).
/// </summary>
/// <param name="Operator">The logical operator to apply.</param>
/// <param name="Operands">
/// The child filter expressions. <see cref="LogicalOperator.Not"/> expects exactly one operand.
/// </param>
public sealed record LogicalExpression(LogicalOperator Operator, IReadOnlyList<FilterExpression> Operands) : FilterExpression;
