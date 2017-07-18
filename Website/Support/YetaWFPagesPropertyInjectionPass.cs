// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

// Portions /* Copyright © 2017 Softel vdm, Inc. - http://yetawf.com/Documentation/YetaWF/Licensing */
// Copied MVC6 PagesPropertyInjectionPass.cs and modified to create a custom YetaWFPagesPropertyInjectionPass
// original formatting kept to simplify diffs

#if MVC6

using Microsoft.AspNetCore.Mvc.Razor.Extensions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace YetaWF2.Support
{
    public class YetaWFPagesPropertyInjectionPass : RazorIRPassBase, IRazorIROptimizationPass
    {
        protected override void ExecuteCore(RazorCodeDocument codeDocument, DocumentIRNode irDocument)
        {
            if (irDocument.DocumentKind != RazorPageDocumentClassifierPass.RazorPageDocumentKind)
            {
                return;
            }

            bool asis;
            var modelType = YetaWFModelDirective.GetModelType(irDocument, out asis);
            var visitor = new Visitor();
            visitor.Visit(irDocument);

            var @class = visitor.Class;

            var viewDataType = $"global::Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary<{modelType}>";
            var vddProperty = new CSharpStatementIRNode()
            {
                Parent = @class
            };
            RazorIRBuilder.Create(vddProperty)
                .Add(new RazorIRToken()
                {
                    Kind = RazorIRToken.TokenKind.CSharp,
                    Content = $"public {viewDataType} ViewData => ({viewDataType})PageContext?.ViewData;",
                });
            @class.Children.Add(vddProperty);

            var modelProperty = new CSharpStatementIRNode()
            {
                Parent = @class
            };
            RazorIRBuilder.Create(modelProperty)
                .Add(new RazorIRToken()
                {
                    Kind = RazorIRToken.TokenKind.CSharp,
                    Content = $"public {modelType} Model => ViewData.Model;",
                });
            @class.Children.Add(modelProperty);
        }

        private class Visitor : RazorIRNodeWalker
        {
            public ClassDeclarationIRNode Class { get; private set; }

            public override void VisitClassDeclaration(ClassDeclarationIRNode node)
            {
                if (Class == null)
                {
                    Class = node;
                }

                base.VisitClassDeclaration(node);
            }
        }
    }
}

#else
#endif