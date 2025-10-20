using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal partial class Program
{
    // =====================================================================
    // REWRITER
    // =====================================================================

    private sealed class NUnitAssertRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (node.Expression is not MemberAccessExpressionSyntax ma)
                return base.VisitInvocationExpression(node);

            // We don't have a semantic model here, so stay text-based but conservative.
            var ownerText = ma.Expression.ToString();
            if (!IsNUnitAssertOwner(ownerText))
                return base.VisitInvocationExpression(node);

            var methodName = ma.Name is GenericNameSyntax g ? g.Identifier.Text : ma.Name.Identifier.Text;
            var typeArgs = (ma.Name as GenericNameSyntax)?.TypeArgumentList?.Arguments;

            var args = node.ArgumentList.Arguments;
            var (trimmedArgs, messageExpr) = ExtractMessageAndArgs(args);

            InvocationExpressionSyntax? transformed = ownerText switch
            {
                "Assert" => TransformAssert(methodName, typeArgs, trimmedArgs, messageExpr, node),
                "StringAssert" => TransformStringAssert(methodName, trimmedArgs, messageExpr, node),
                "CollectionAssert" => TransformCollectionAssert(methodName, trimmedArgs, messageExpr, node),
                _ => null
            };

            return transformed is null
                ? base.VisitInvocationExpression(node)
                : transformed.WithTriviaFrom(node);
        }

        private static bool IsNUnitAssertOwner(string text)
            => text is "Assert" or "StringAssert" or "CollectionAssert";

        // -----------------------------------------------------------------
        // Message extraction:
        // - If >= 3 args, allow mid-position message w/ params → string.Format
        // - If trailing string and >= 2 args, treat as message (no params)
        // - If only 2 args, DO NOT treat first string as message (it's likely "expected")
        // -----------------------------------------------------------------
        private static (SeparatedSyntaxList<ArgumentSyntax> trimmed, ExpressionSyntax? messageExpr)
            ExtractMessageAndArgs(SeparatedSyntaxList<ArgumentSyntax> args)
        {
            // Case A: message with params (need at least 3 args)
            if (args.Count >= 3)
            {
                // Search for last string-like before the final position (so trailing args can be params)
                for (int i = args.Count - 2; i >= 0; i--)
                {
                    if (IsStringy(args[i].Expression))
                    {
                        var message = args[i].Expression;
                        var trailing = args.Skip(i + 1).Select(a => a.Expression).ToList();

                        var formatted = trailing.Count > 0
                            ? ParseExpr($"string.Format({message.ToFullString()}, {string.Join(", ", trailing.Select(t => t.ToFullString()))})")
                            : message;

                        var kept = new SeparatedSyntaxList<ArgumentSyntax>().AddRange(args.Take(i));
                        return (kept, formatted);
                    }
                }
            }

            // Case B: trailing single message (no params): need at least 2 args
            if (args.Count >= 2 && IsStringy(args.Last().Expression))
            {
                var msg = args.Last().Expression;
                var kept = new SeparatedSyntaxList<ArgumentSyntax>().AddRange(args.Take(args.Count - 1));
                return (kept, msg);
            }

            // No message detected
            return (args, null);

            static bool IsStringy(ExpressionSyntax e)
                => e is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.StringLiteralExpression)
                || e is InterpolatedStringExpressionSyntax;
        }

        // -----------------------------------------------------------------
        // Assert.* conversions
        // -----------------------------------------------------------------
        private static InvocationExpressionSyntax? TransformAssert(
            string method,
            SeparatedSyntaxList<TypeSyntax>? typeArgs,
            SeparatedSyntaxList<ArgumentSyntax> args,
            ExpressionSyntax? message,
            InvocationExpressionSyntax original)
        {
            switch (method)
            {
                case "IsNull" when args.Count == 1: Inc("Assert.IsNull → That(Is.Null)"); return That(args[0].Expression, "Is.Null", message);
                case "IsNotNull" when args.Count == 1: Inc("Assert.IsNotNull → That(Is.Not.Null)"); return That(args[0].Expression, "Is.Not.Null", message);
                case "IsTrue" when args.Count == 1: Inc("Assert.IsTrue → That(Is.True)"); return That(args[0].Expression, "Is.True", message);
                case "IsFalse" when args.Count == 1: Inc("Assert.IsFalse → That(Is.False)"); return That(args[0].Expression, "Is.False", message);
                case "IsEmpty" when args.Count == 1: Inc("Assert.IsEmpty → That(Is.Empty)"); return That(args[0].Expression, "Is.Empty", message);
                case "IsNotEmpty" when args.Count == 1: Inc("Assert.IsNotEmpty → That(Is.Not.Empty)"); return That(args[0].Expression, "Is.Not.Empty", message);

                case "AreEqual": return TransformAreEqual(args, message, original);
                case "AreNotEqual": return TransformAreNotEqual(args, message, original);

                case "AreSame" when args.Count == 2: Inc("Assert.AreSame → That(Is.SameAs)"); return That(args[1].Expression, "Is.SameAs", args[0].Expression, message);
                case "AreNotSame" when args.Count == 2: Inc("Assert.AreNotSame → That(Is.Not.SameAs)"); return That(args[1].Expression, "Is.Not.SameAs", args[0].Expression, message);
                case "Greater" when args.Count == 2: Inc("Assert.Greater → That(Is.GreaterThan)"); return That(args[0].Expression, "Is.GreaterThan", args[1].Expression, message);
                case "Less" when args.Count == 2: Inc("Assert.Less → That(Is.LessThan)"); return That(args[0].Expression, "Is.LessThan", args[1].Expression, message);
                case "GreaterOrEqual" when args.Count == 2:
                    Inc("Assert.GreaterOrEqual → That(Is.GreaterThanOrEqualTo)");
                    return That(args[0].Expression, "Is.GreaterThanOrEqualTo", args[1].Expression, message);
                case "LessOrEqual" when args.Count == 2:
                    Inc("Assert.LessOrEqual → That(Is.LessThanOrEqualTo)");
                    return That(args[0].Expression, "Is.LessThanOrEqualTo", args[1].Expression, message);

                // Exceptions
                case "Throws": return TransformThrows(typeArgs, args, isAsync: false, message) ?? original;
                case "ThrowsAsync": return TransformThrows(typeArgs, args, isAsync: true, message) ?? original;
                case "Catch": return TransformThrows(typeArgs, args, isAsync: false, message) ?? original; // switch to InstanceOf<T>() if you want subtypes
                case "DoesNotThrow": return TransformDoesNotThrow(args, isAsync: false, message) ?? original;
                case "DoesNotThrowAsync": return TransformDoesNotThrow(args, isAsync: true, message) ?? original;

                default: return original;
            }
        }

        private static InvocationExpressionSyntax TransformAreEqual(
            SeparatedSyntaxList<ArgumentSyntax> args,
            ExpressionSyntax? message,
            InvocationExpressionSyntax original)
        {
            if (args.Count < 2) return original;

            var expected = args[0].Expression;
            var actual = args[1].Expression;

            if (args.Count >= 3)
            {
                var delta = args[2].Expression;
                var constraint = ParseExpr($"Is.EqualTo({expected}).Within({delta})");
                Inc("Assert.AreEqual(tol) → That(EqualTo.Within)");
                return That(actual, constraint, message);
            }

            Inc("Assert.AreEqual → That(Is.EqualTo)");
            return That(actual, "Is.EqualTo", expected, message);
        }

        private static InvocationExpressionSyntax TransformAreNotEqual(
            SeparatedSyntaxList<ArgumentSyntax> args,
            ExpressionSyntax? message,
            InvocationExpressionSyntax original)
        {
            if (args.Count < 2) return original;

            var expected = args[0].Expression;
            var actual = args[1].Expression;

            if (args.Count >= 3)
            {
                var delta = args[2].Expression;
                var constraint = ParseExpr($"Is.Not.EqualTo({expected}).Within({delta})");
                Inc("Assert.AreNotEqual(tol) → That(Not.EqualTo.Within)");
                return That(actual, constraint, message);
            }

            Inc("Assert.AreNotEqual → That(Is.Not.EqualTo)");
            return That(actual, "Is.Not.EqualTo", expected, message);
        }

        // -----------------------------------------------------------------
        // Exceptions
        // -----------------------------------------------------------------
        private static InvocationExpressionSyntax? TransformThrows(
            SeparatedSyntaxList<TypeSyntax>? typeArgs,
            SeparatedSyntaxList<ArgumentSyntax> args,
            bool isAsync,
            ExpressionSyntax? message)
        {
            if (args.Count == 0) return null;

            ExpressionSyntax? action = null;
            ExpressionSyntax? nonGenericTypeOf = null;
            string? genericTypeText = null;

            if (typeArgs.HasValue && typeArgs.Value.Count == 1)
            {
                genericTypeText = typeArgs.Value[0].ToString();
                action = args[0].Expression;
            }
            else if (args.Count >= 2 && args[0].Expression is TypeOfExpressionSyntax tof)
            {
                nonGenericTypeOf = tof;
                action = args[1].Expression;
            }
            else if (args.Count >= 1)
            {
                // Unknown shape → leave as-is
                return null;
            }

            if (action is null) return null;

            var lambda = EnsureLambda(action, isAsync);

            ExpressionSyntax throwsConstraint = genericTypeText is not null
                ? ParseExpr($"Throws.TypeOf<{genericTypeText}>()")
                : ParseExpr($"Throws.TypeOf({nonGenericTypeOf})");

            Inc(isAsync ? "Assert.ThrowsAsync → That(Throws.TypeOf)" : "Assert.Throws → That(Throws.TypeOf)");
            return That(lambda, throwsConstraint, message);
        }

        private static InvocationExpressionSyntax? TransformDoesNotThrow(
            SeparatedSyntaxList<ArgumentSyntax> args,
            bool isAsync,
            ExpressionSyntax? message)
        {
            if (args.Count < 1) return null;
            var lambda = EnsureLambda(args[0].Expression, isAsync);
            Inc(isAsync ? "Assert.DoesNotThrowAsync → That(Throws.Nothing)" : "Assert.DoesNotThrow → That(Throws.Nothing)");
            return That(lambda, "Throws.Nothing", message);
        }

        // -----------------------------------------------------------------
        // StringAssert.*
        // -----------------------------------------------------------------
        private static InvocationExpressionSyntax? TransformStringAssert(
            string method,
            SeparatedSyntaxList<ArgumentSyntax> args,
            ExpressionSyntax? message,
            InvocationExpressionSyntax original)
        {
            if (args.Count < 2) return original;

            var a0 = args[0].Expression;
            var a1 = args[1].Expression;

            return method switch
            {
                "Contains" => IncR("StringAssert.Contains → That(Does.Contain)", That(a1, "Does.Contain", a0, message)),
                "StartsWith" => IncR("StringAssert.StartsWith → That(Does.StartWith)", That(a1, "Does.StartWith", a0, message)),
                "EndsWith" => IncR("StringAssert.EndsWith → That(Does.EndWith)", That(a1, "Does.EndWith", a0, message)),
                "Matches" or "IsMatch"
                               => IncR("StringAssert.Matches → That(Does.Match)", That(a1, "Does.Match", a0, message)),
                "DoesNotMatch" or "IsNotMatch"
                               => IncR("StringAssert.DoesNotMatch → That(Does.Not.Match)", That(a1, "Does.Not.Match", a0, message)),
                _ => original
            };

            static InvocationExpressionSyntax IncR(string key, InvocationExpressionSyntax r) { Inc(key); return r; }
        }

        // -----------------------------------------------------------------
        // CollectionAssert.*
        // -----------------------------------------------------------------
        private static InvocationExpressionSyntax? TransformCollectionAssert(
            string method,
            SeparatedSyntaxList<ArgumentSyntax> args,
            ExpressionSyntax? message,
            InvocationExpressionSyntax original)
        {
            if (args.Count < 2 && method != "AreEquivalent") return original;

            return method switch
            {
                "Contains" when args.Count >= 2 => IncR("CollectionAssert.Contains → That(Does.Contain)",
                                                              That(args[0].Expression, "Does.Contain", args[1].Expression, message)),
                "DoesNotContain" when args.Count >= 2 => IncR("CollectionAssert.DoesNotContain → That(Does.Not.Contain)",
                                                              That(args[0].Expression, "Does.Not.Contain", args[1].Expression, message)),
                "AreEquivalent" when args.Count >= 2 => IncR("CollectionAssert.AreEquivalent → That(Is.EquivalentTo)",
                                                              That(args[1].Expression, "Is.EquivalentTo", args[0].Expression, message)),
                _ => original
            };

            static InvocationExpressionSyntax IncR(string key, InvocationExpressionSyntax r) { Inc(key); return r; }
        }

        // -----------------------------------------------------------------
        // Build Assert.That(...)
        // -----------------------------------------------------------------
        private static InvocationExpressionSyntax That(ExpressionSyntax actual, string simpleConstraint, ExpressionSyntax? message)
            => That(actual, ParseExpr(simpleConstraint), message);

        private static InvocationExpressionSyntax That(ExpressionSyntax actual, string constraintHead, ExpressionSyntax expectedOrArg, ExpressionSyntax? message)
            => That(actual, constraintHead.Contains("(") ? ParseExpr(constraintHead) : ParseExpr($"{constraintHead}({expectedOrArg})"), message);

        private static InvocationExpressionSyntax That(ExpressionSyntax actual, ExpressionSyntax constraint, ExpressionSyntax? message)
        {
            var args = SyntaxFactory.SeparatedList(new[]
            {
                SyntaxFactory.Argument(actual),
                SyntaxFactory.Argument(constraint)
            }.Concat(message is null ? Array.Empty<ArgumentSyntax>() : new[] { SyntaxFactory.Argument(message) }));

            return SyntaxFactory.InvocationExpression(
                       SyntaxFactory.MemberAccessExpression(
                           SyntaxKind.SimpleMemberAccessExpression,
                           SyntaxFactory.IdentifierName("Assert"),
                           SyntaxFactory.IdentifierName("That")))
                   .WithArgumentList(SyntaxFactory.ArgumentList(args));
        }

        private static ExpressionSyntax EnsureLambda(ExpressionSyntax expr, bool isAsync)
        {
            if (expr is ParenthesizedLambdaExpressionSyntax or SimpleLambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
                return expr;

            var lambda = SyntaxFactory.ParenthesizedLambdaExpression()
                                      .WithParameterList(SyntaxFactory.ParameterList())
                                      .WithExpressionBody(expr);
            return isAsync ? lambda.WithAsyncKeyword(SyntaxFactory.Token(SyntaxKind.AsyncKeyword)) : lambda;
        }

        private static ExpressionSyntax ParseExpr(string code)
            => (ExpressionSyntax)SyntaxFactory.ParseExpression(code);

        // Stats tap-through (expects Program.Inc to exist in your partial Program)
        private static void Inc(string key) => Program.Inc(key);
    }
}
