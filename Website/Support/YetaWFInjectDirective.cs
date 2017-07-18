// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// Portions /* Copyright � 2017 Softel vdm, Inc. - http://yetawf.com/Documentation/YetaWF/Licensing */
// Copied MVC6 InjectDirective.cs and modified to create a custom YetaWFInjectDirective
// original formatting kept to simplify diffs

#if MVC6

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.AspNetCore.Mvc.Razor.Extensions;

namespace YetaWF2.Support
{
    public static class YetaWFInjectDirective
    {
        public static readonly DirectiveDescriptor Directive = DirectiveDescriptor.CreateDirective(
            "inject",
            DirectiveKind.SingleLine,
            builder => builder.AddTypeToken().AddMemberToken());

        public static IRazorEngineBuilder Register(IRazorEngineBuilder builder)
        {
            builder.AddDirective(Directive);
            builder.Features.Add(new Pass());
            return builder;
        }

        internal class Pass : RazorIRPassBase, IRazorDirectiveClassifierPass
        {
            // Runs after the @model and @namespace directives
            public override int Order => 10;
            
            protected override void ExecuteCore(RazorCodeDocument codeDocument, DocumentIRNode irDocument)
            {
                var visitor = new Visitor();
                visitor.Visit(irDocument);
                bool asis;
                var modelType = YetaWFModelDirective.GetModelType(irDocument, out asis);

                var properties = new HashSet<string>(StringComparer.Ordinal);

                for (var i = visitor.Directives.Count - 1 ; i >= 0 ; i--)
                {
                    var directive = visitor.Directives[i];
                    var tokens = directive.Tokens.ToArray();
                    if (tokens.Length < 2)
                    {
                        continue;
                    }

                    var typeName = tokens[0].Content;
                    var memberName = tokens[1].Content;

                    if (!properties.Add(memberName))
                    {
                        continue;
                    }

                    typeName = typeName.Replace("<TModel>", "<" + modelType + ">");

                    var injectNode = new InjectDirectiveIRNode()
                    {
                        TypeName = typeName,
                        MemberName = memberName,
                        Parent = visitor.Class,
                    };

                    visitor.Class.Children.Add(injectNode);
                }
            }
        }

        private class Visitor : RazorIRNodeWalker
        {
            public ClassDeclarationIRNode Class { get; private set; }

            public IList<DirectiveIRNode> Directives { get; } = new List<DirectiveIRNode>();

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
                    Directives.Add(node);
                }
            }
        }
    }
}

#else
#endif