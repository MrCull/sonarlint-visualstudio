﻿/*
 * SonarLint for Visual Studio
 * Copyright (C) 2016-2020 SonarSource SA
 * mailto:info AT sonarsource DOT com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.CodeAnalysis.RuleSets;

namespace SonarLint.VisualStudio.Integration.Binding
{
    internal class RuleSetReferenceChecker : IRuleSetReferenceChecker
    {
        private readonly ISolutionRuleSetsInformationProvider ruleSetInfoProvider;
        private readonly IRuleSetSerializer ruleSetSerializer;

        public RuleSetReferenceChecker(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }

            ruleSetInfoProvider = serviceProvider.GetService<ISolutionRuleSetsInformationProvider>();
            ruleSetInfoProvider.AssertLocalServiceIsNotNull();

            ruleSetSerializer = serviceProvider.GetService<IRuleSetSerializer>();
            ruleSetSerializer.AssertLocalServiceIsNotNull();
        }

        public bool IsReferencedByAllDeclarations(Project project, string targetRuleSetFilePath)
        {
            var declarations = ruleSetInfoProvider.GetProjectRuleSetsDeclarations(project).ToArray();

            var isRuleSetBound = declarations.Length > 0 &&
                                 declarations.All(declaration => IsReferenced(project, declaration, targetRuleSetFilePath));

            return isRuleSetBound;
        }

        public bool IsReferenced(Project project, RuleSetDeclaration declaration, string targetRuleSetFilePath)
        {
            var projectRuleSet = FindDeclarationRuleSet(project, declaration);

            return projectRuleSet != null && HasInclude(projectRuleSet, targetRuleSetFilePath);
        }

        private RuleSet FindDeclarationRuleSet(Project project, RuleSetDeclaration declaration)
        {
            // Check if project rule set is found (we treat missing/erroneous rule set settings as not found)
            if (!ruleSetInfoProvider.TryGetProjectRuleSetFilePath(project, declaration, out var ruleSetFilePath))
            {
                return null;
            }

            return ruleSetSerializer.LoadRuleSet(ruleSetFilePath);
        }

        private bool HasInclude(RuleSet source, string targetRuleSetFilePath)
        {
            Debug.Assert(Path.IsPathRooted(source.FilePath));
            Debug.Assert(Path.IsPathRooted(targetRuleSetFilePath));

            // The path in the RuleSetInclude could be relative or absolute.
            // If relative, we assume it's relative to the source ruleset file.
            var sourceDirectory = Path.GetDirectoryName(source.FilePath);
            var canonicalTargetFilePath = Path.GetFullPath(targetRuleSetFilePath);

            // Special case: the target ruleset is the one we are looking for
            if (IsMatchingPath(source.FilePath, canonicalTargetFilePath, sourceDirectory))
            {
                return true;
            }

            var matchingRuleSetIncludes = source.RuleSetIncludes
                .Where(i => IsMatchingPath(i.FilePath, canonicalTargetFilePath, sourceDirectory))
                .ToList();

            Debug.Assert(matchingRuleSetIncludes.Count < 2, "Not expecting to find multiple RuleSetInclude matching the filter");
            return matchingRuleSetIncludes.Count != 0;
        }

        private static bool IsMatchingPath(string candidate, string canonicalAbsoluteTargetPath, string sourceDirectory)
        {
            Debug.Assert(Path.IsPathRooted(canonicalAbsoluteTargetPath));

            if (!Path.IsPathRooted(candidate))
            {
                candidate = Path.Combine(sourceDirectory, candidate);
            }

            // Make sure the path is in a canonical form
            candidate = Path.GetFullPath(candidate);

            return StringComparer.OrdinalIgnoreCase.Equals(candidate, canonicalAbsoluteTargetPath);
        }
    }
}