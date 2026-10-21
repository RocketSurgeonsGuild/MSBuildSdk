using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rocket.Surgery.MyAssembly;

internal static class Extensions
{
    private static TMember AddSimple<TMember>(this TMember member, XmlElementSyntax xmlElement) where TMember : MemberDeclarationSyntax
    {
        return member.WithLeadingTrivia(
            SyntaxFactory.TriviaList(
                SyntaxFactory.Trivia(
                    SyntaxFactory.DocumentationComment(
                        xmlElement,
                        SyntaxFactory.XmlText()
                                     .WithTextTokens(
                                          SyntaxFactory.TokenList(
                                              SyntaxFactory.Token(
                                                  SyntaxFactory.TriviaList(),
                                                  SyntaxKind.XmlTextLiteralNewLineToken,
                                                  "\n",
                                                  "\n",
                                                  SyntaxFactory.TriviaList()
                                              )
                                          )
                                      )
                    )
                )
            )
        );
    }

    public static PropertyDeclarationSyntax AddValue(this PropertyDeclarationSyntax property, string value)
    {
        return property.AddSimple(
            SyntaxFactory.XmlValueElement(
                SyntaxFactory.XmlText(value)
            )
        );
    }

    public static TMember AddSummary<TMember>(this TMember member, string value) where TMember : MemberDeclarationSyntax
    {
        return member.AddSimple(
            SyntaxFactory.XmlSummaryElement(
                SyntaxFactory.XmlText(value)
            )
        );
    }
}
