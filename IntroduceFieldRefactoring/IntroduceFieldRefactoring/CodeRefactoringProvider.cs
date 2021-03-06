
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using System;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntroduceFieldRefactoring
{
	[ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(IntroduceFieldRefactoringCodeRefactoringProvider)), Shared]
	internal class IntroduceFieldRefactoringCodeRefactoringProvider : CodeRefactoringProvider
	{
		public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
		{
			var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
			var node = root.FindNode(context.Span);

			ParameterSyntax parameter = null;
			if (node is ParameterListSyntax pls)
			{
				var parameterList = pls;

				parameter = parameterList.Parameters.SingleOrDefault(p => p.Span.Contains(context.Span));
				if (parameter == null)
				{
					return;
				}
			}
			else if (node is ParameterSyntax ps)
			{
				parameter = ps;
			}
			else if (node is IdentifierNameSyntax ins)
			{
				var identifier = ins;
				parameter = identifier.Parent as ParameterSyntax;
			}

			if (parameter == null)
			{
				return;
			}

			var parameterName = GetParameterName(parameter);
			if (string.IsNullOrEmpty(parameterName) || !(parameter.Parent.Parent is ConstructorDeclarationSyntax))
			{
				return;
			}

			if (!VariableExists(root, "_" + parameterName))
			{
				var action = CodeAction.Create("Introduce and initialize field '_" + parameterName + "'", ct => CreateFieldAsync(context, parameter, parameterName, ct, true));
				context.RegisterRefactoring(action);
			}

			//if (!VariableExists(root, parameterName))
			//{
			//	var action2 = CodeAction.Create("Introduce and initialize field 'this." + parameterName + "'", ct => CreateFieldAsync(context, parameter, parameterName, ct));
			//	context.RegisterRefactoring(action2);
			//}
		}

		private async Task<Document> CreateFieldAsync(CodeRefactoringContext context, ParameterSyntax parameter,
			 string paramName, CancellationToken cancellationToken, bool useUnderscore = false)
		{
			var guard = CreateGuard(context, paramName, useUnderscore);
			var oldConstructor = parameter.Ancestors().OfType<ConstructorDeclarationSyntax>().First();
			var newConstructor = oldConstructor.WithBody(oldConstructor.Body.AddStatements(
				  guard));

			var oldClass = parameter.FirstAncestorOrSelf<ClassDeclarationSyntax>();
			var oldClassWithNewCtor = oldClass.ReplaceNode(oldConstructor, newConstructor);

			var fieldDeclaration = CreateFieldDeclaration(GetParameterType(parameter), paramName, useUnderscore);

			var lastFieldDeclaration = oldClassWithNewCtor.Members.OfType<FieldDeclarationSyntax>().LastOrDefault();
			var newClassMembers = oldClassWithNewCtor.Members;
			if (lastFieldDeclaration == null)
				newClassMembers = oldClassWithNewCtor.Members.Insert(0, fieldDeclaration);
			else
				newClassMembers = oldClassWithNewCtor.Members.Insert(oldClassWithNewCtor.Members.IndexOf(lastFieldDeclaration) + 1, fieldDeclaration);

			var newClass = oldClassWithNewCtor
				 .WithMembers(newClassMembers)
				 .WithAdditionalAnnotations(Formatter.Annotation);

			var oldRoot = await context.Document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
			var newRoot = oldRoot.ReplaceNode(oldClass, newClass);

			return context.Document.WithSyntaxRoot(newRoot);
		}

		private StatementSyntax CreateGuard(CodeRefactoringContext context, string paramName, bool useUnderscore)
		{
			//return
			//SyntaxFactory.IfStatement(
			//                            SyntaxFactory.BinaryExpression(
			//                                SyntaxKind.EqualsExpression,
			//                                SyntaxFactory.IdentifierName(paramName),
			//                                SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)
			//                             ),
			//                            SyntaxFactory.ThrowStatement(
			//                                SyntaxFactory.ObjectCreationExpression(
			//                                    SyntaxFactory.IdentifierName(nameof(ArgumentNullException)),
			//                                                                 SyntaxFactory.ArgumentList().AddArguments(
			//                                                                     SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(SyntaxFactory.IdentifierName(paramName).Identifier.ToString())))
			//                                                                 ),
			//                                                                 null
			//                                                                      )
			//                                                          )
			//                                                 );

			var expression =
				 SyntaxFactory.AssignmentExpression(
					  SyntaxKind.SimpleAssignmentExpression,
					  useUnderscore ? (ExpressionSyntax)SyntaxFactory.IdentifierName("_" + paramName) : SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.ThisExpression(), SyntaxFactory.IdentifierName(paramName)),
					  
			  SyntaxFactory.InvocationExpression(
					SyntaxFactory.MemberAccessExpression(
						 SyntaxKind.SimpleMemberAccessExpression,
						 SyntaxFactory.IdentifierName(
							  @"Guard"),
						 SyntaxFactory.IdentifierName(
							  @"ArgumentNotNull"))
					.WithOperatorToken(
						 SyntaxFactory.Token(
							  SyntaxKind.DotToken)))
			  .WithArgumentList(
					SyntaxFactory.ArgumentList(
						 SyntaxFactory.SeparatedList(new[] {
							  SyntaxFactory.Argument(
									SyntaxFactory.LiteralExpression(
										 SyntaxKind.StringLiteralExpression,
										 SyntaxFactory.Literal(
											  SyntaxFactory.TriviaList(),
											  paramName,
											  paramName,
											  SyntaxFactory.TriviaList()))),

							  SyntaxFactory.Argument(
									SyntaxFactory.LiteralExpression(
										 SyntaxKind.StringLiteralExpression,
										 SyntaxFactory.Literal(
											  SyntaxFactory.TriviaList(),
											  $"nameof({paramName})",
											  $"nameof({paramName})",
											  SyntaxFactory.TriviaList())))
						 }))
					.WithOpenParenToken(
						 SyntaxFactory.Token(
							  SyntaxKind.OpenParenToken))
					.WithCloseParenToken(
						 SyntaxFactory.Token(
							  SyntaxKind.CloseParenToken))));

			return SyntaxFactory.ExpressionStatement(expression);
		}

		private ExpressionSyntax CreateAssignment(CodeRefactoringContext context, string paramName, bool useUnderscore)
		{
			ExpressionSyntax assignment =
				 SyntaxFactory.AssignmentExpression(
					  SyntaxKind.SimpleAssignmentExpression,
					  useUnderscore ? (ExpressionSyntax)SyntaxFactory.IdentifierName("_" + paramName) : SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.ThisExpression(), SyntaxFactory.IdentifierName(paramName)),
					  SyntaxFactory.IdentifierName(paramName)
				 );
			return assignment;
		}

		public static bool VariableExists(SyntaxNode root, params string[] variableNames)
		{
			return root
				 .DescendantNodes()
				 .OfType<VariableDeclarationSyntax>()
				 .SelectMany(ps => ps.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken) && variableNames.Contains(t.ValueText)))
				 .Any();
		}

		public static string GetParameterType(ParameterSyntax parameter)
		{
			return parameter.DescendantNodes().First(node => node is TypeSyntax).GetFirstToken().ValueText;
		}

		private static string GetParameterName(ParameterSyntax parameter)
		{
			return parameter.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Last().ValueText;
		}

		private static FieldDeclarationSyntax CreateFieldDeclaration(string type, string name, bool useUnderscore = false)
		{
			return SyntaxFactory.FieldDeclaration(
				 SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName(type))
				 .WithVariables(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(SyntaxFactory.Identifier(useUnderscore ? "_" + name : name)))))
				 .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));
		}
	}
}