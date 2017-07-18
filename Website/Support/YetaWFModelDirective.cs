// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// Portions /* Copyright © 2017 Softel vdm, Inc. - http://yetawf.com/Documentation/YetaWF/Licensing */
// Copied MVC6 ModelDirective.cs and modified to create a custom YetaWFModelDirective
// original formatting kept to simplify diffs

#if MVC6

using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using System.Text.RegularExpressions;

namespace YetaWF2.Support
{
    public static class YetaWFModelDirective
    {
        public static readonly DirectiveDescriptor Directive = DirectiveDescriptor.CreateDirective(
            "model",
            DirectiveKind.SingleLine,
            builder => builder.AddTypeToken());

        public static IRazorEngineBuilder Register(IRazorEngineBuilder builder)
        {
            builder.AddDirective(Directive);
            builder.Features.Add(new Pass(builder.DesignTime));
            return builder;
        }

        public static string GetModelType(DocumentIRNode document, out bool asis)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var visitor = new Visitor();
            return GetModelType(document, visitor, out asis);
        }

        private static string GetModelType(DocumentIRNode document, Visitor visitor, out bool asis)
        {
            asis = false;
            visitor.Visit(document);

            for (var i = visitor.ModelDirectives.Count - 1 ; i >= 0 ; i--)
            {
                var directive = visitor.ModelDirectives[i];

                var tokens = directive.Tokens.ToArray();
                if (tokens.Length >= 1)
                {
                    return tokens[0].Content;
                }
            }

            if (document.DocumentKind == RazorPageDocumentClassifierPass.RazorPageDocumentKind)
            {
                return visitor.Class.Name;
            }
            else
            {
                //$$$MOD
                Match match = reInherits1.Match(visitor.Class.BaseType);
                if (match.Success) {
                    asis = true;
                    return match.Groups["model"].Value;
                } else
                //$$$END-MOD

                return "dynamic";
            }
        }
        //$$$MOD
        private static Regex reInherits1 = new Regex(@"^\s*[a-zA-Z0-9_\.]+\<(\s*[a-zA-Z0-9_\.]+\s*\,){0,1}\s*(?'model'[a-zA-Z0-9_\.\<\> \,]+\?{0,1})\s*\>\s*$", RegexOptions.Multiline);
        //$$$END-MOD

        internal class Pass : RazorIRPassBase, IRazorDirectiveClassifierPass
        {
            private readonly bool _designTime;

            public Pass(bool designTime)
            {
                _designTime = designTime;
            }

            // Runs after the @inherits directive
            public override int Order => 5;

            protected override void ExecuteCore(RazorCodeDocument codeDocument, DocumentIRNode irDocument)
            {
                var visitor = new Visitor();
                bool asis;
                var modelType = GetModelType(irDocument, visitor, out asis);

                if (_designTime)
                {
                    // Alias the TModel token to a known type.
                    // This allows design time compilation to succeed for Razor files where the token isn't replaced.
                    var typeName = $"global::{typeof(object).FullName}";
                    var usingNode = new UsingStatementIRNode()
                    {
                        Content = $"TModel = {typeName}"
                    };

                    visitor.Namespace?.Children.Insert(0, usingNode);
                }
                if (asis) return;

                var baseType = visitor.Class?.BaseType?.Replace("<TModel>", "<" + modelType + ">");
                for (var i = visitor.InheritsDirectives.Count - 1 ; i >= 0 ; i--)
                {
                    var directive = visitor.InheritsDirectives[i];
                    var tokens = directive.Tokens.ToArray();
                    if (tokens.Length >= 1)
                    {
                        baseType = tokens[0].Content.Replace("<TModel>", "<" + modelType + ">");
                        tokens[0].Content = baseType;
                        break;
                    }
                }

                visitor.Class.BaseType = baseType;
            }
        }

        private class Visitor : RazorIRNodeWalker
        {
            public NamespaceDeclarationIRNode Namespace { get; private set; }

            public ClassDeclarationIRNode Class { get; private set; }

            public IList<DirectiveIRNode> InheritsDirectives { get; } = new List<DirectiveIRNode>();

            public IList<DirectiveIRNode> ModelDirectives { get; } = new List<DirectiveIRNode>();

            public override void VisitNamespaceDeclaration(NamespaceDeclarationIRNode node)
            {
                if (Namespace == null)
                {
                    Namespace = node;
                }

                base.VisitNamespaceDeclaration(node);
            }

            public override void VisitClassDeclaration(ClassDeclarationIRNode node)
            {
                if (Class == null)
                {
                    Class = node;
                }

                base.VisitClassDeclaration(node);
            }

            public override void VisitDirective(DirectiveIRNode node)
            {
                if (node.Descriptor == Directive)
                {
                    ModelDirectives.Add(node);
                }
                else if (node.Descriptor.Name == "inherits")
                {
                    InheritsDirectives.Add(node);
                }
            }
        }
    }
}

#else
#endif